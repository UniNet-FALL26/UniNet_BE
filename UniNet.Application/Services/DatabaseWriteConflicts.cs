using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace UniNet.Application.Services;

internal static class DatabaseWriteConflicts
{
    public static bool IsWriteConflict(Exception ex) => ex is DbUpdateConcurrencyException ||
        ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } ||
        (ex is DbUpdateException or InvalidOperationException && ex.InnerException != null && IsWriteConflict(ex.InnerException));
}
