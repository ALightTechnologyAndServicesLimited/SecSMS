using System;
using System.Security.Cryptography;

namespace SecSMS.Common.Totp;

/// <summary>
/// RFC 6238 TOTP implementation (HMAC-SHA1/256/512 + dynamic truncation).
/// The generated code is returned only to the caller (copy/send handlers);
/// callers must never bind it to visible UI.
/// </summary>
public static class TotpGenerator
{
    public static string Generate(TotpEntry entry, DateTimeOffset utcNow)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        return Generate(entry.SecretBase32, utcNow, entry.Algorithm, entry.Digits, entry.Period);
    }

    public static string Generate(string secretBase32, DateTimeOffset utcNow, TotpAlgorithm algorithm = TotpAlgorithm.Sha1, int digits = 6, int period = 30)
    {
        if (digits is < 6 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), "Digits must be 6-8.");
        }

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be positive.");
        }

        var key = Base32Decode(secretBase32);
        try
        {
            var unixSeconds = utcNow.ToUnixTimeSeconds();
            var counter = unixSeconds / period;
            return GenerateForCounter(key, counter, algorithm, digits);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static int GetRemainingSeconds(DateTimeOffset utcNow, int period = 30)
    {
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        var unixSeconds = utcNow.ToUnixTimeSeconds();
        var elapsed = (int)(unixSeconds % period);
        return period - elapsed;
    }

    internal static string GenerateForCounter(byte[] key, long counter, TotpAlgorithm algorithm, int digits)
    {
        // Counter as 8-byte big-endian.
        var counterBytes = new byte[8];
        var c = counter;
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(c & 0xFF);
            c >>= 8;
        }

        byte[] hash;
        using (HMAC hmac = algorithm switch
        {
            TotpAlgorithm.Sha256 => new HMACSHA256(key),
            TotpAlgorithm.Sha512 => new HMACSHA512(key),
            _ => new HMACSHA1(key),
        })
        {
            hash = hmac.ComputeHash(counterBytes);
        }

        try
        {
            var offset = hash[hash.Length - 1] & 0x0F;
            var binary =
                ((hash[offset] & 0x7F) << 24) |
                ((hash[offset + 1] & 0xFF) << 16) |
                ((hash[offset + 2] & 0xFF) << 8) |
                (hash[offset + 3] & 0xFF);

            var modulo = (int)Math.Pow(10, digits);
            var otp = binary % modulo;
            return otp.ToString(new string('0', digits));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    internal static byte[] Base32Decode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("TOTP secret is required.", nameof(input));
        }

        // Normalize: ignore whitespace/hyphens, uppercase, strip padding.
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch) || ch == '-')
            {
                continue;
            }

            if (ch == '=')
            {
                break;
            }

            sb.Append(char.ToUpperInvariant(ch));
        }

        var clean = sb.ToString();
        if (clean.Length == 0)
        {
            throw new FormatException("TOTP secret is empty after normalization.");
        }

        var output = new byte[clean.Length * 5 / 8 + 1];
        var buffer = 0;
        var bitsLeft = 0;
        var count = 0;

        foreach (var ch in clean)
        {
            var val = CharToValue(ch);
            if (val < 0)
            {
                throw new FormatException($"Invalid Base32 character '{ch}'.");
            }

            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output[count++] = (byte)((buffer >> (bitsLeft - 8)) & 0xFF);
                bitsLeft -= 8;
            }
        }

        if (count == 0)
        {
            throw new FormatException("TOTP secret did not decode to any bytes.");
        }

        var result = new byte[count];
        Buffer.BlockCopy(output, 0, result, 0, count);
        CryptographicOperations.ZeroMemory(output);
        return result;
    }

    static int CharToValue(char c)
    {
        if (c >= 'A' && c <= 'Z')
        {
            return c - 'A';
        }

        if (c >= '2' && c <= '7')
        {
            return c - '2' + 26;
        }

        return -1;
    }
}
