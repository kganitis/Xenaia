namespace Xenaia.Modules.Triage;

public static class TriageConstants
{
    /// <summary>Tag stamped on every triaged ticket; tickets carrying it are
    /// skipped on later polls (idempotency lives on the ticket, not in a DB).</summary>
    public const string MarkerTag = "xenaia-triaged";
}
