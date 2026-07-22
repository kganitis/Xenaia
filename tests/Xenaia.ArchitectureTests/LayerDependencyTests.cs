using System.Reflection;

namespace Xenaia.ArchitectureTests;

/// <summary>
/// Enforces the dependency rules from the design spec (section 3).
/// Checks compiled assembly references, so an unused ProjectReference is
/// invisible until code actually uses it; the rules arm themselves as the
/// codebase grows.
/// </summary>
public class LayerDependencyTests
{
    public static TheoryData<string, string[]> ForbiddenReferences => new()
    {
        // The kernel references no other Xenaia assembly at all.
        { "Xenaia.Core", new[] { "Xenaia." } },
        { "Xenaia.Core.Ai", new[] { "Xenaia.Domain", "Xenaia.Modules", "Xenaia.Adapters", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer", "Xenaia.Extensions" } },
        { "Xenaia.Domain.Bookings", new[] { "Xenaia.Core.Ai", "Xenaia.Modules", "Xenaia.Adapters", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer", "Xenaia.Extensions" } },
        { "Xenaia.Modules.Triage", new[] { "Xenaia.Adapters", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer" } },
        { "Xenaia.Modules.Concierge", new[] { "Xenaia.Adapters", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer" } },
        // Sync may reference Core + Domain.Bookings only (spec section 2): no
        // adapters, no Data, no hosts, and no other module.
        { "Xenaia.Modules.Sync", new[] { "Xenaia.Modules.Triage", "Xenaia.Modules.Concierge", "Xenaia.Modules.AgentQa", "Xenaia.Modules.Rostering", "Xenaia.Adapters", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer" } },
        { "Xenaia.Modules.AgentQa", new[] { "Xenaia.Adapters", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer" } },
        { "Xenaia.Modules.Rostering", new[] { "Xenaia.Adapters", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer" } },
        { "Xenaia.Data", new[] { "Xenaia.Modules", "Xenaia.Adapters", "Xenaia.Api", "Xenaia.McpServer" } },
        { "Xenaia.Data.PostgreSql", new[] { "Xenaia.Modules", "Xenaia.Adapters", "Xenaia.Api", "Xenaia.McpServer" } },
        { "Xenaia.Extensions.Abstractions", new[] { "Xenaia.Modules", "Xenaia.Adapters", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer" } },
        { "Xenaia.Adapters.Freshdesk", new[] { "Xenaia.Core.Ai", "Xenaia.Domain", "Xenaia.Modules.Concierge", "Xenaia.Modules.Sync", "Xenaia.Modules.AgentQa", "Xenaia.Modules.Rostering", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer", "Xenaia.Extensions" } },
        // BrightTide implements IBookingSystemProvider (owned by Domain.Bookings);
        // it may reference Domain.Bookings + Core only, no modules or other layers.
        { "Xenaia.Adapters.BrightTide", new[] { "Xenaia.Core.Ai", "Xenaia.Modules", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer", "Xenaia.Extensions" } },
        // GoogleWorkspace implements ISpreadsheetGateway (owned by Modules.Sync);
        // it may reference Modules.Sync + transitive only, no other modules or layers.
        { "Xenaia.Adapters.GoogleWorkspace", new[] { "Xenaia.Core.Ai", "Xenaia.Domain", "Xenaia.Modules.Triage", "Xenaia.Modules.Concierge", "Xenaia.Modules.AgentQa", "Xenaia.Modules.Rostering", "Xenaia.Data", "Xenaia.Api", "Xenaia.McpServer", "Xenaia.Extensions" } },
    };

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void Assembly_never_references_forbidden_layers(string assemblyName, string[] forbiddenPrefixes)
    {
        var offending = Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(r => r.Name ?? "")
            .Where(name => forbiddenPrefixes.Any(name.StartsWith))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Assemblies_load_by_name()
    {
        // Positive control: if loading by name broke, every rule above would
        // pass vacuously. This test fails loudly instead.
        Assert.NotNull(Assembly.Load("Xenaia.Core"));
        Assert.NotNull(Assembly.Load("Xenaia.Domain.Bookings"));
    }
}
