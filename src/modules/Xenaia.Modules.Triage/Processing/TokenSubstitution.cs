using System.Text.RegularExpressions;

namespace Xenaia.Modules.Triage.Processing;

public static class TokenSubstitution
{
    private static readonly Regex Token = new(
        @"\{([A-Za-z][A-Za-z0-9]*)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Replaces {captureName} tokens; absent captures become "".</summary>
    public static string Substitute(string template, IReadOnlyDictionary<string, string> captures) =>
        Token.Replace(template, m => captures.GetValueOrDefault(m.Groups[1].Value, ""));
}
