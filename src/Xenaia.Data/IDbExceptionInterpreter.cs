using Microsoft.EntityFrameworkCore;

namespace Xenaia.Data;

/// <summary>
/// Provider-specific classification of EF save failures. Xenaia.Data is
/// provider-agnostic (it references no concrete database driver), so the
/// SQLSTATE/error-code inspection lives in each provider assembly and is
/// injected through this port. The default <see cref="NullDbExceptionInterpreter"/>
/// classifies nothing, so providers that do not supply one simply propagate
/// the raw <see cref="DbUpdateException"/>.
/// </summary>
public interface IDbExceptionInterpreter
{
    /// <summary>True when the failure is a unique/primary-key violation
    /// (Postgres <c>23505</c> and equivalents): an insert lost a race against a
    /// concurrent writer.</summary>
    bool IsUniqueViolation(DbUpdateException exception);
}

/// <summary>No-op interpreter used when no provider supplies one.</summary>
public sealed class NullDbExceptionInterpreter : IDbExceptionInterpreter
{
    public bool IsUniqueViolation(DbUpdateException exception) => false;
}
