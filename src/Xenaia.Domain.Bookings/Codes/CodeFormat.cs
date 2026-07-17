using System.Text.RegularExpressions;

namespace Xenaia.Domain.Bookings.Codes;

/// <summary>
/// A tenant-configured code grammar. Tenant regex is untrusted input:
/// compiled once, always evaluated with a hard timeout, and invalid
/// patterns fail at construction (and therefore at startup, because
/// options validation constructs formats before the host serves traffic).
/// </summary>
public sealed class CodeFormat
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly Regex _regex;

    public string Pattern { get; }

    private CodeFormat(string pattern, Regex regex)
    {
        Pattern = pattern;
        _regex = regex;
    }

    public static CodeFormat Create(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidCodeFormatException("Code pattern is empty.");

        try
        {
            var regex = new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                MatchTimeout);
            return new CodeFormat(pattern, regex);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidCodeFormatException(
                $"Code pattern '{pattern}' is not a valid regular expression: {ex.Message}");
        }
    }

    /// <summary>On timeout the value does not match (fail closed).</summary>
    public bool Matches(string value)
    {
        try
        {
            return _regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
