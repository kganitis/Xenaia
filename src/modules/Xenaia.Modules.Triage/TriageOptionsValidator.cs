using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xenaia.Modules.Triage.Processing;
using Xenaia.Modules.Triage.Rules;

namespace Xenaia.Modules.Triage;

/// <summary>
/// Fail-closed startup gate for triage: the rule pack must load and validate,
/// every processor a rule names must be registered, processor names must be
/// unique, and urgency needs at least one date format. Runs at ValidateOnStart.
///
/// Resolves processors lazily from IServiceProvider rather than taking
/// IEnumerable&lt;ITicketProcessor&gt; as a constructor dependency: processors
/// (e.g. BookingUrgencyProcessor) themselves depend on IOptions&lt;TriageOptions&gt;,
/// so an eager constructor dependency here would make OptionsFactory's eager
/// construction of every IValidateOptions&lt;TriageOptions&gt; recurse back into
/// building IOptions&lt;TriageOptions&gt; and the DI container would reject it as
/// a circular dependency. Deferring the lookup to Validate() breaks the cycle:
/// by the time Validate runs, the IOptions&lt;TriageOptions&gt; singleton already
/// exists (its .Value merely hasn't been computed yet), so processors can
/// resolve it as a constructor dependency without re-entering construction.
/// </summary>
public sealed class TriageOptionsValidator(IServiceProvider serviceProvider)
    : IValidateOptions<TriageOptions>
{
    public ValidateOptionsResult Validate(string? name, TriageOptions options)
    {
        var errors = new List<string>();

        // BookingLookupProcessor is scoped (its dependencies are EF/adapter
        // scoped services); resolving it straight off the root provider
        // would throw when scope validation is enabled, so validation gets
        // its own scope. Singleton processors resolve the same instance
        // either way.
        using var scope = serviceProvider.CreateScope();
        var processors = scope.ServiceProvider.GetServices<ITicketProcessor>();
        var registered = processors.Select(p => p.Name).ToList();
        errors.AddRange(registered
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"Duplicate processor name '{g.Key}' registered."));

        if (options.Urgency.DateTimeFormats.Count == 0
            || options.Urgency.DateTimeFormats.Any(string.IsNullOrWhiteSpace))
            errors.Add("Tenant:Triage:Urgency:DateTimeFormats must contain at least one non-empty format.");

        RulePack? pack = null;
        try
        {
            pack = RulePackLoader.Load(options.RulePackPath);
        }
        catch (RulePackValidationException ex)
        {
            errors.AddRange(ex.Errors);
        }

        if (pack is not null)
        {
            var known = registered.ToHashSet(StringComparer.Ordinal);
            errors.AddRange(pack.Rules
                .Where(r => r.ProcessorName is not null && !known.Contains(r.ProcessorName))
                .Select(r => $"Rule '{r.Id}' references unregistered processor '{r.ProcessorName}'."));
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
