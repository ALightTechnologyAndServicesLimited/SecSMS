using System;

namespace SecSMS.Common.Totp;

public sealed class TotpEntry
{
    public TotpEntry()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    public TotpEntry(string id, string issuer, string accountName, string secretBase32, TotpAlgorithm algorithm, int digits, int period)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        Issuer = issuer ?? string.Empty;
        AccountName = accountName ?? string.Empty;
        SecretBase32 = secretBase32 ?? string.Empty;
        Algorithm = algorithm;
        Digits = digits;
        Period = period;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Issuer { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    /// <summary>
    /// Base32-encoded shared secret (RFC 4648, case-insensitive).
    /// Never log this value.
    /// </summary>
    public string SecretBase32 { get; set; } = string.Empty;

    public TotpAlgorithm Algorithm { get; set; } = TotpAlgorithm.Sha1;

    public int Digits { get; set; } = 6;

    public int Period { get; set; } = 30;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(AccountName))
            {
                return $"{Issuer} — {AccountName}";
            }

            return string.IsNullOrWhiteSpace(Issuer) ? AccountName : Issuer;
        }
    }
}
