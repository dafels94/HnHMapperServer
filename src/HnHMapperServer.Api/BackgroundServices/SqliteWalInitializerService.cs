using HnHMapperServer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Api.BackgroundServices;

/// <summary>
/// Background service to enable SQLite WAL mode on startup.
/// WAL mode allows concurrent reads during writes, which is essential for imports.
/// </summary>
public class SqliteWalInitializerService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SqliteWalInitializerService> _logger;

    public SqliteWalInitializerService(IServiceProvider serviceProvider, ILogger<SqliteWalInitializerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            // Enable WAL mode for better concurrency (allows concurrent reads during writes)
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            // Set busy timeout to 30 seconds (wait instead of failing immediately on lock)
            await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=30000;", cancellationToken);
            // Optimize for concurrent access
            await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);

            _logger.LogInformation("SQLite WAL mode enabled with 30s busy timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable SQLite WAL mode");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
