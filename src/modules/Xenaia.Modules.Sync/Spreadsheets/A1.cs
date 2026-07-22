using System.Text.RegularExpressions;

namespace Xenaia.Modules.Sync.Spreadsheets;

/// <summary>Helpers for composing Google Sheets A1-notation ranges.</summary>
internal static partial class A1
{
    /// <summary>
    /// Quotes a sheet/tab name for use in an A1 range. Google's A1 syntax
    /// requires the tab name to be wrapped in single quotes whenever it
    /// contains anything beyond letters, digits, and underscores (a space,
    /// punctuation, and so on); an unquoted <c>My Sheet!A:G</c> is misparsed.
    /// A single quote inside the name is escaped by doubling it. A name that
    /// is already safe is returned unchanged.
    /// </summary>
    public static string QuoteTab(string name)
    {
        if (SafeName().IsMatch(name))
            return name;
        return $"'{name.Replace("'", "''")}'";
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex SafeName();
}
