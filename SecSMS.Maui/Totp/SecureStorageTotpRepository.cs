using System.Text;
using System.Text.Json;
using SecSMS.Common.Totp;

namespace SecSMS.Maui.Totp;

/// <summary>
/// Stores TOTP enrollments in MAUI SecureStorage (Keystore/Keychain backed).
/// Secrets are never written to Preferences, logs, or UI.
/// </summary>
public sealed class SecureStorageTotpRepository : ITotpRepository
{
    const string StorageKey = "secsms.totp.v1";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task<IReadOnlyList<TotpEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var json = await SecureStorage.Default.GetAsync(StorageKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<TotpEntry>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<TotpEntry>>(json, JsonOptions);
            return list ?? (IReadOnlyList<TotpEntry>)Array.Empty<TotpEntry>();
        }
        catch
        {
            // Corrupted payload: do not crash, treat as empty. User can re-enroll.
            return Array.Empty<TotpEntry>();
        }
    }

    public async Task<TotpEntry> AddAsync(TotpEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.SecretBase32))
        {
            throw new ArgumentException("TOTP secret is required.", nameof(entry));
        }

        // Validate secret before persisting.
        _ = TotpGenerator.Generate(entry, DateTimeOffset.UtcNow);

        var list = (await ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            entry.Id = Guid.NewGuid().ToString("N");
        }

        // Replace an entry with the same Id (rename), or the same secret
        // (re-scanning/re-importing mints a new Id each time, so the secret is
        // the dedupe key). Id match wins; keep the stored Id so references stay stable.
        var normalizedSecret = NormalizeSecret(entry.SecretBase32);
        var existing = list.FindIndex(e => string.Equals(e.Id, entry.Id, StringComparison.Ordinal));
        if (existing < 0)
        {
            existing = list.FindIndex(e => string.Equals(NormalizeSecret(e.SecretBase32), normalizedSecret, StringComparison.Ordinal));
        }
        if (existing >= 0)
        {
            entry.Id = list[existing].Id;
            list[existing] = entry;
        }
        else
        {
            list.Add(entry);
        }

        await SaveAsync(list).ConfigureAwait(false);
        return entry;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var list = (await ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var removed = list.RemoveAll(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        if (removed > 0)
        {
            await SaveAsync(list).ConfigureAwait(false);
        }
    }

    static string NormalizeSecret(string secret)
    {
        var sb = new StringBuilder(secret.Length);
        foreach (var ch in secret)
        {
            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '=')
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(ch));
        }

        return sb.ToString();
    }

    static Task SaveAsync(List<TotpEntry> list)
    {
        var json = JsonSerializer.Serialize(list, JsonOptions);
        return SecureStorage.Default.SetAsync(StorageKey, json);
    }
}
