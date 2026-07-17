using System.Text.RegularExpressions;

namespace Xenaia.Modules.Triage.Rules;

/// <summary>
/// A tenant-authored pattern. Tenant regex is untrusted input to the engine:
/// compiled once, always evaluated under a hard timeout, and invalid patterns
/// fail at load time (and therefore at startup). On timeout the input does
/// not match (fail closed), mirroring CodeFormat in Domain.Bookings.
/// </summary>
public sealed class TimedRegex
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly Regex _regex;

    public string Pattern { get; }

    private TimedRegex(string pattern, Regex regex)
    {
        Pattern = pattern;
        _regex = regex;
    }

    /// <summary>Throws ArgumentException when the pattern is not valid regex.</summary>
    public static TimedRegex Create(string pattern)
    {
        try
        {
            var regex = new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                MatchTimeout);
            return new TimedRegex(pattern, regex);
        }
        catch (ArgumentException ex) when (ex is not ArgumentNullException)
        {
            // .NET throws the more specific RegexParseException (a subtype of
            // ArgumentException) for malformed patterns; normalize to the
            // documented ArgumentException contract.
            throw new ArgumentException($"Invalid regex pattern '{pattern}': {ex.Message}", nameof(pattern), ex);
        }
    }

    public IReadOnlyList<string> CaptureNames =>
        _regex.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToList();

    /// <summary>Null when the input does not match or evaluation timed out.</summary>
    public Match? Match(string input, out bool timedOut)
    {
        timedOut = false;
        try
        {
            var match = _regex.Match(input);
            return match.Success ? match : null;
        }
        catch (RegexMatchTimeoutException)
        {
            timedOut = true;
            return null;
        }
    }
}
