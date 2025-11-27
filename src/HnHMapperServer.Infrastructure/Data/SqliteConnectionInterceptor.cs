using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace HnHMapperServer.Infrastructure.Data;

/// <summary>
/// Interceptor that applies SQLite performance optimizations via PRAGMA commands
/// when a database connection is opened.
/// </summary>
public class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplySqlitePragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ApplySqlitePragmas(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void ApplySqlitePragmas(DbConnection connection)
    {
        // Only apply to SQLite connections
        if (connection.GetType().Name != "SqliteConnection")
            return;

        using var command = connection.CreateCommand();

        // Set journal mode to WAL for better concurrency
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();

        // Set synchronous mode to NORMAL for faster writes (acceptable durability)
        command.CommandText = "PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();

        // Use memory for temporary tables
        command.CommandText = "PRAGMA temp_store=MEMORY;";
        command.ExecuteNonQuery();

        // Set cache size to 64MB (negative value = KB)
        command.CommandText = "PRAGMA cache_size=-64000;";
        command.ExecuteNonQuery();

        // Enable memory-mapped I/O (256MB)
        command.CommandText = "PRAGMA mmap_size=268435456;";
        command.ExecuteNonQuery();
    }
}
