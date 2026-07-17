namespace Xenaia.Modules.Triage.Rules;

public sealed class RulePackValidationException(IReadOnlyList<string> errors)
    : Exception("Rule pack validation failed:\n" + string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
