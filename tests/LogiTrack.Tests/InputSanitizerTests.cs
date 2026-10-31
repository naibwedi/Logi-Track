using System.Diagnostics;
using System.Net;
using logirack.Services;
using Xunit;

namespace LogiTrack.Tests;

public class InputSanitizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n  ")]
    public void Sanitize_NullOrWhitespace_ReturnsEmptyString(string? input)
    {
        var result = InputSanitizer.Sanitize(input);
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>", "alert(&#39;xss&#39;)")]
    [InlineData("<SCRIPT SRC=\"http://evil.com/xss.js\"></SCRIPT>", "")]
    [InlineData("<script type=\"text/javascript\" async>alert(1)</script>", "alert(1)")]
    [InlineData("<script>eval(atob('YWxlcnQoMSk='))</script>", "eval(atob(&#39;YWxlcnQoMSk=&#39;))")]
    public void Sanitize_ScriptTags_AreCompletelyStripped(string input, string expected)
    {
        var result = InputSanitizer.Sanitize(input);
        Assert.Equal(expected, result);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</script>", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<iframe src=\"https://evil.com\"></iframe>", "")]
    [InlineData("<IFRAME SRC=\"javascript:alert(1)\"></IFRAME>", "")]
    [InlineData("<iframe src=\"evil.html\" onload=\"alert(1)\" />", "")]
    public void Sanitize_IframeTags_AreCompletelyStripped(string input, string expected)
    {
        var result = InputSanitizer.Sanitize(input);
        Assert.Equal(expected, result);
        Assert.DoesNotContain("<iframe", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</iframe>", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<img src=\"x\" onerror=\"alert('xss')\">", "")]
    [InlineData("<svg/onload=alert('xss')>", "")]
    [InlineData("<body onload=\"alert('xss')\">", "")]
    [InlineData("<a href=\"javascript:alert(1)\">Click me</a>", "Click me")]
    [InlineData("<div onmouseover=\"alert('pwned')\">Hover here</div>", "Hover here")]
    public void Sanitize_DangerousAttributesAndTags_AreStripped(string input, string expected)
    {
        var result = InputSanitizer.Sanitize(input);
        Assert.Equal(expected, result);
        Assert.DoesNotContain("<img", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_NestedTagBypassAttempts_NeutralizesExecutableHtml()
    {
        // Attack payload attempting to survive a single regex replace pass: <<script>script>alert(1)</script>
        var nestedPayload = "<<script>script>alert(1)</script>";
        var result = InputSanitizer.Sanitize(nestedPayload);

        // Even if residual tags remain, HTML encoding converts < and > into &lt; and &gt;
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
    }

    [Fact]
    public void Sanitize_UnclosedTags_HtmlEncodedSafely()
    {
        var unclosed = "<script src=\"https://evil.com/xss.js\"";
        var result = InputSanitizer.Sanitize(unclosed);

        // Unclosed tags must be HTML encoded so browsers cannot interpret them as HTML tags
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", result);
    }

    [Fact]
    public void Sanitize_ControlCharacters_AreFilteredExceptWhitelisted()
    {
        // Control chars: null byte (0x00), backspace (0x08), bell (0x07), escape (0x1B)
        // Whitelisted: newline (\n), carriage return (\r), tab (\t)
        var inputWithControl = "Line 1\r\n\tTabbed\0\a\b\x1BLine 2";
        var result = InputSanitizer.Sanitize(inputWithControl);

        Assert.Contains("Line 1\r\n\tTabbedLine 2", result);
        Assert.False(result.Contains('\0'));
        Assert.False(result.Contains('\a'));
        Assert.False(result.Contains('\b'));
        Assert.False(result.Contains('\x1B'));
    }

    [Fact]
    public void Sanitize_StringExceedingDefaultMaxLength_IsTruncatedTo1000Chars()
    {
        var longInput = new string('A', 1500);
        var result = InputSanitizer.Sanitize(longInput);

        Assert.Equal(1000, result.Length);
        Assert.Equal(new string('A', 1000), result);
    }

    [Fact]
    public void Sanitize_StringExactly1000Chars_PreservesFullLength()
    {
        var exactInput = new string('B', 1000);
        var result = InputSanitizer.Sanitize(exactInput);

        Assert.Equal(1000, result.Length);
        Assert.Equal(exactInput, result);
    }

    [Fact]
    public void Sanitize_CustomMaxLengthEnforced()
    {
        var input = "Short address but exceeds max limit";
        var result = InputSanitizer.Sanitize(input, maxLength: 10);

        Assert.Equal(10, result.Length);
        Assert.Equal("Short addr", result);
    }

    [Fact]
    public void Sanitize_HtmlSpecialCharacters_AreProperlyEncoded()
    {
        var input = "Warehouse & Logistics <Norway> \"Express\" 'Fast'";
        var result = InputSanitizer.Sanitize(input);

        // <Norway> gets stripped as a tag by HtmlTagRegex, special chars encoded
        Assert.Contains("&amp;", result);
        Assert.Contains("&quot;Express&quot;", result);
        Assert.Contains("&#39;Fast&#39;", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
    }

    [Fact]
    public void Sanitize_UnicodeAndInternationalCharacters_PreservedWhenDecoded()
    {
        var input = "Storgata 42, Oslo æøå ÆØÅ — Frágil / 100€";
        var result = InputSanitizer.Sanitize(input);

        // WebUtility.HtmlEncode encodes non-ASCII characters to safe HTML entities (&#230;, etc.)
        // When decoded for display, the original text is preserved faithfully
        var decoded = WebUtility.HtmlDecode(result);
        Assert.Equal(input, decoded);
    }

    [Theory]
    [InlineData("'; DROP TABLE Trips; --", "&#39;; DROP TABLE Trips; --")]
    [InlineData("1' OR '1'='1", "1&#39; OR &#39;1&#39;=&#39;1")]
    [InlineData("<script>/*--></script><script>alert(1)</script>", "/*--&gt;alert(1)")]
    [InlineData("<math><mtext><table><mglyph><style><!--</style><img src=1 onerror=alert(1)>", "")]
    public void Sanitize_AdversarialInjectionPayloads_AreRenderedHarmless(string payload, string expectedOutput)
    {
        var result = InputSanitizer.Sanitize(payload);
        Assert.Equal(expectedOutput, result);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_MassiveInputWithMalformedTags_ExecutesQuicklyWithoutReDoS()
    {
        // 50,000 characters containing repetitive unclosed and malformed brackets
        var malformedChunk = "<<<<<tag attr=\"val\">>>>>";
        var massivePayload = string.Concat(Enumerable.Repeat(malformedChunk, 2000));

        var stopwatch = Stopwatch.StartNew();
        var sanitized = InputSanitizer.Sanitize(massivePayload, maxLength: 500);
        stopwatch.Stop();

        Assert.Equal(500, sanitized.Length);
        // Must execute in well under 250ms (demonstrates O(N) regex evaluation without catastrophic backtracking)
        Assert.True(stopwatch.ElapsedMilliseconds < 250, $"Execution took {stopwatch.ElapsedMilliseconds}ms, exceeding 250ms threshold.");
    }
}
