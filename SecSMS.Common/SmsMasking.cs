using System.Text.RegularExpressions;

namespace SecSMS.Common;

public static class SmsMasking
{
    static readonly Regex DigitRunRegex = new("\\d{3,}", RegexOptions.Compiled);
    static readonly Regex OtpRegex = new("\\d{4,8}", RegexOptions.Compiled);
    static readonly Regex OtpNearWordRegex = new("(?i)otp\\D*(\\d{4,8})", RegexOptions.Compiled);

    public static string MaskDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return DigitRunRegex.Replace(input, m => new string('*', m.Length));
    }

    public static string? ExtractOtp(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        // 1. Prefer a code that appears near the word "OTP" (case-insensitive)
        var otpWordMatch = OtpNearWordRegex.Match(input);
        if (otpWordMatch.Success && otpWordMatch.Groups.Count > 1)
        {
            return otpWordMatch.Groups[1].Value;
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
