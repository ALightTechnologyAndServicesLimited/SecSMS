using System;
using System.Collections.Generic;

namespace SecSMS.Common.Totp;

/// <summary>
/// Parses otpauth://totp/... URIs produced by most 2FA enrollment QR codes.
/// See https://github.com/google/google-authenticator/wiki/Key-Uri-Format
/// </summary>
public static class OtpauthParser
{
    public static bool TryParse(string? uri, out TotpEntry? entry, out string? error)
    {
        entry = null;
        error = null;

        if (string.IsNullOrWhiteSpace(uri))
        {
            error = "QR code is empty.";
            return false;
        }

        uri = uri.Trim();
        if (!uri.StartsWith("otpauth://totp/", StringComparison.OrdinalIgnoreCase))
        {
            error = "QR code is not a TOTP enrollment code (expected otpauth://totp/...).";
            return false;
        }

        try
        {
            // Split label and query manually to avoid Uri quirks with ':' in label.
            var withoutScheme = uri.Substring("otpauth://totp/".Length);
            string label;
            string query;
            var qIndex = withoutScheme.IndexOf('?');
            if (qIndex >= 0)
            {
                label = withoutScheme.Substring(0, qIndex);
                query = withoutScheme.Substring(qIndex + 1);
            }
            else
            {
                label = withoutScheme;
                query = string.Empty;
            }

            label = Uri.UnescapeDataString(label);
            var parameters = ParseQuery(query);

            if (!parameters.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
            {
                error = "QR code is missing the secret parameter.";
                return false;
            }

            secret = secret.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);

            // Validate secret early so bad QR codes are rejected before saving.
            try
            {
                var key = TotpGenerator.Base32Decode(secret);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            }
            catch (Exception ex)
            {
                error = "QR code has an invalid secret: " + ex.Message;
                return false;
            }

            var issuer = parameters.TryGetValue("issuer", out var qIssuer) ? qIssuer ?? string.Empty : string.Empty;
            var account = string.Empty;

            if (!string.IsNullOrEmpty(label))
            {
                // Label formats: "Issuer:account" or just "account".
                var colon = label.IndexOf(':');
                if (colon >= 0)
                {
                    var labelIssuer = label.Substring(0, colon).Trim();
                    account = label.Substring(colon + 1).Trim();
                    if (string.IsNullOrWhiteSpace(issuer))
                    {
                        issuer = labelIssuer;
                    }
                }
                else
                {
                    account = label.Trim();
                }
            }

            issuer = issuer.Trim();
            account = account.Trim();

            var algorithm = TotpAlgorithm.Sha1;
            if (parameters.TryGetValue("algorithm", out var algoRaw) && !string.IsNullOrWhiteSpace(algoRaw))
            {
                var a = algoRaw.Trim().ToUpperInvariant().Replace("-", string.Empty);
                algorithm = a switch
                {
                    "SHA1" => TotpAlgorithm.Sha1,
                    "SHA256" => TotpAlgorithm.Sha256,
                    "SHA512" => TotpAlgorithm.Sha512,
                    _ => throw new FormatException($"Unsupported algorithm '{algoRaw}'. Use SHA1/SHA256/SHA512."),
                };
            }

            var digits = 6;
            if (parameters.TryGetValue("digits", out var digitsRaw) && !string.IsNullOrWhiteSpace(digitsRaw))
            {
                if (!int.TryParse(digitsRaw.Trim(), out digits) || digits is < 6 or > 8)
                {
                    error = "QR code has an invalid digits parameter (expected 6-8).";
                    return false;
                }
            }

            var period = 30;
            if (parameters.TryGetValue("period", out var periodRaw) && !string.IsNullOrWhiteSpace(periodRaw))
            {
                if (!int.TryParse(periodRaw.Trim(), out period) || period <= 0 || period > 120)
                {
                    error = "QR code has an invalid period parameter.";
                    return false;
                }
            }

            entry = new TotpEntry(
                Guid.NewGuid().ToString("N"),
                issuer,
                account,
                secret,
                algorithm,
                digits,
                period);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = "Unable to parse QR code: " + ex.Message;
            return false;
        }
    }

    static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[Uri.UnescapeDataString(pair).Trim().ToLowerInvariant()] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(pair.Substring(0, eq)).Trim();
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
            result[key] = value;
        }

        return result;
    }
}
