using Microsoft.Extensions.Options;

namespace Xenaia.Modules.Triage.Rules;

/// <summary>The validated, compiled rule pack for this deployment.</summary>
public interface IRulePackProvider
{
    RulePack Pack { get; }
}

/// <summary>Loads once, lazily; TriageOptionsValidator has already proven the
/// file valid before the host starts serving.</summary>
public sealed class RulePackProvider(IOptions<TriageOptions> options) : IRulePackProvider
{
    private readonly Lazy<RulePack> _pack = new(
        () => RulePackLoader.Load(options.Value.RulePackPath),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public RulePack Pack => _pack.Value;
}
