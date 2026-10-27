using System.Net;
using System.Text.RegularExpressions;

namespace logirack.Services;

/// <summary>
/// Utility service to sanitize user input strings, strip dangerous HTML tags,
/// normalize control characters, and prevent injection attacks.
/// </summary>
public static class InputSanitizer
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes user-provided input by trimming whitespace, removing control characters,
    /// stripping HTML tags, HTML-encoding entities, and enforcing maximum string length.
    /// </summary>
    /// <param name="input">Raw string input from user.</param>
    /// <param name="maxLength">Maximum allowed character length.</param>
    /// <returns>Sanitized string.</returns>
    public static string Sanitize(string? input, int maxLength = 1000)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();

        // Strip non-printable control characters except newline and tab
        var filtered = new string(trimmed
            .Where(c => !char.IsControl(c) || c == '\r' || c == '\n' || c == '\t')
            .ToArray());

        // Strip HTML markup
        var stripped = HtmlTagRegex.Replace(filtered, string.Empty);

        // HTML-encode to neutralize any residual special markup characters
        var encoded = WebUtility.HtmlEncode(stripped);

        if (encoded.Length > maxLength)
        {
            encoded = encoded[..maxLength];
        }

        return encoded.Trim();
    }
}
