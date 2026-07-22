using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Xenaia.Data.PostgreSql;

/// <summary>Classifies Npgsql save failures. A <see cref="DbUpdateException"/>
/// whose inner exception is a <see cref="PostgresException"/> carrying SQLSTATE
/// <c>23505</c> is a unique-violation insert race.</summary>
public sealed class PostgresDbExceptionInterpreter : IDbExceptionInterpreter
{
    public bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
