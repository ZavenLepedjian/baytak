using System.Security.Cryptography;

namespace Baytak.Api.Domain;

/// <summary>
/// The code an administrator shares (in the building's WhatsApp group, for example) so residents can find the building and
/// ask to join it. It only names the building: every request still waits for the administrator's approval.
/// </summary>
public static class InviteCodes
{
    /// <summary>How long a code works after it was made.</summary>
    public const int Hours = 8;

    public const int Length = 8;

    /// <summary>Letters and digits that cannot be mistaken for one another when read from a phone screen (no 0, O, 1, I or L).</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string New()
    {
        Span<char> code = stackalloc char[Length];
        for (var i = 0; i < Length; i++) code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(code);
    }

    /// <summary>
    /// The code as stored, from what a person typed or a link carried: upper case, without spaces or dashes ("abcd-2345" is
    /// "ABCD2345"). Null when it cannot be a code.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var code = new string(input.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return code.Length == Length && code.All(c => Alphabet.Contains(c)) ? code : null;
    }
}
