using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SecSMS.Common;

public static class SmsMasking
{
    static readonly Regex DigitRunRegex = new("\\d{3,}", RegexOptions.Compiled);
    static readonly Regex OtpRegex = new("\\d{4,8}", RegexOptions.Compiled);
    static readonly Regex OtpNearWordRegex = new("(?i)(?:otp\\D*(\\d{4,8})|(\\d{4,8})\\D*otp)", RegexOptions.Compiled);
    static readonly Regex UrlRegex = new("(?i)https?://\\S+", RegexOptions.Compiled);
    static readonly char[] UrlTrimChars = new[] { '.', ',', ';', ':', ')', ']', '}', '>', '"', '\'', '!' };

    public static string MaskDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return DigitRunRegex.Replace(input, m => new string('*', m.Length));
    }

    public static string MaskSensitiveText(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var matches = UrlRegex.Matches(input);
        if (matches.Count == 0)
        {
            return MaskDigits(input);
        }

        var sb = new StringBuilder(input.Length);
        var idx = 0;

        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            if (match.Index < idx)
            {
                continue;
            }

            sb.Append(MaskDigits(input.Substring(idx, match.Index - idx)));

            var raw = match.Value;
            var (url, suffix) = SplitUrlSuffix(raw);
            sb.Append(MaskUrl(url));
            sb.Append(suffix);

            idx = match.Index + raw.Length;
        }

        if (idx < input.Length)
        {
            sb.Append(MaskDigits(input.Substring(idx)));
        }

        return sb.ToString();
    }

    public static string MaskUrls(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return UrlRegex.Replace(input, m =>
        {
            var (url, suffix) = SplitUrlSuffix(m.Value);
            return MaskUrl(url) + suffix;
        });
    }

    public static IReadOnlyList<string> ExtractAllUrls(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        var matches = UrlRegex.Matches(input);
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var (url, _) = SplitUrlSuffix(match.Value);
            if (!string.IsNullOrWhiteSpace(url) && !list.Contains(url))
            {
                list.Add(url);
            }
        }

        return list;
    }

    static (string Url, string Suffix) SplitUrlSuffix(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return (string.Empty, string.Empty);
        }

        var end = raw.Length;
        while (end > 0)
        {
            var ch = raw[end - 1];
            if (Array.IndexOf(UrlTrimChars, ch) < 0)
            {
                break;
            }

            end--;
        }

        if (end <= 0)
        {
            return (string.Empty, raw);
        }

        return (raw.Substring(0, end), raw.Substring(end));
    }

    static string MaskUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var hostPart = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            return $"{uri.Scheme}://{hostPart}****";
        }

        return "****";
    }

    public static string? ExtractOtp(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        // 1. Prefer a code that appears near the word "OTP" (case-insensitive),
        //    either immediately after or immediately before the word.
        var otpWordMatch = OtpNearWordRegex.Match(input);
        if (otpWordMatch.Success)
        {
            // Group 1: digits after "OTP"; Group 2: digits before "OTP".
            if (otpWordMatch.Groups.Count > 1 && otpWordMatch.Groups[1].Success)
            {
                return otpWordMatch.Groups[1].Value;
            }
            if (otpWordMatch.Groups.Count > 2 && otpWordMatch.Groups[2].Success)
            {
                return otpWordMatch.Groups[2].Value;
            }
        }

        // 2. Otherwise, if there are multiple numeric sequences, take the last one
        var matches = OtpRegex.Matches(input);
        if (matches.Count == 0)
        {
            return null;
        }

        return matches[matches.Count - 1].Value;
    }

	public static IReadOnlyList<string> ExtractAllOtps(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return Array.Empty<string>();
		}

		var list = new List<string>();
		var matches = OtpRegex.Matches(input);
		foreach (Match match in matches)
		{
			if (match.Success)
			{
				list.Add(match.Value);
			}
		}

		return list;
	}
}
