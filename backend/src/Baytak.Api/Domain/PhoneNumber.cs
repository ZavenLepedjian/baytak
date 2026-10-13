using System.Text.RegularExpressions;

namespace Baytak.Api.Domain;

public static partial class PhoneNumber
{
    [GeneratedRegex(@"^\+?[0-9]{6,15}$")]
    private static partial Regex Valid();

    /// <summary>
    /// Normalises user input to a canonical form used as the account identity and SMS destination.
    /// "+961 3 123 456" -> "+9613123456"; "03 123 456" -> "+9613123456" when the default country code is +961.
    /// Returns null when the input is not a plausible phone number.
    /// </summary>
    public static string? Normalize(string? input, string? defaultCountryCode = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var plus = input.TrimStart().StartsWith('+');
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0) return null;

        string result;
        if (plus) result = "+" + digits;
        else if (digits.StartsWith("00") && digits.Length > 2) result = "+" + digits[2..];
        else if (!string.IsNullOrWhiteSpace(defaultCountryCode))
        {
            // A local number: at least 6 digits of its own, before the country code is added.
            var national = digits.TrimStart('0');
            if (national.Length < 6) return null;
            result = "+" + new string(defaultCountryCode.Where(char.IsAsciiDigit).ToArray()) + national;
        }
        else result = digits;

        return Valid().IsMatch(result) ? result : null;
    }

    /// <summary>Hides all but the last 3 digits for logs.</summary>
    public static string Mask(string? phone) =>
        string.IsNullOrEmpty(phone) ? "" : phone.Length <= 3 ? "***" : new string('*', phone.Length - 3) + phone[^3..];
}
