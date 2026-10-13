using System.Text;

namespace Baytak.Api.Domain;

public static class Text
{
    /// <summary>
    /// Removes control characters and collapses runs of whitespace, so "Tea&#9;&#10; time" becomes "Tea time".
    /// With <paramref name="keepNewlines"/> single line breaks survive (used for free-text notes).
    /// </summary>
    public static string Clean(string? text, bool keepNewlines = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true; // also trims leading whitespace
        foreach (var c in text)
        {
            if (c == '\n' && keepNewlines)
            {
                while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
                if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                lastWasSpace = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else if (!char.IsControl(c))
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}
