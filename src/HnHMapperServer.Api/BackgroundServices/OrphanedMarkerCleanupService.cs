using System.Diagnostics;
using HnHMapperServer.Services.Interfaces;

namespace HnHMapperServer.Api.BackgroundServices;

/// <summary>
/// Background service that periodically cleans up orphaned markers (markers with missing grids).
/// Runs every hour to identify and remove markers that reference non-existent grids.
/// </summary>
public class OrphanedMarkerCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrphanedMarkerCleanupService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    public OrphanedMarkerCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrphanedMarkerCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 30 minutes after startup to allow grids to be uploaded first
        var startupDelay = TimeSpan.FromMinutes(30);
        _logger.LogInformation("Orphaned Marker Cleanup Service starting in {Delay:F1} minutes", startupDelay.TotalMinutes);
        await Task.Delay(startupDelay, stoppingToken);

        _logger.LogInformation("Orphaned Marker Cleanup Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("Orphaned marker cleanup job started");

                using var scope = _scopeFactory.CreateScope();
                var markerService = scope.ServiceProvider.GetRequiredService<IMarkerService>();
                var tenantService = scope.ServiceProvider.GetRequiredService<ITenantService>();

                // Get all active tenants
                var tenants = await tenantService.GetAllTenantsAsync();
                var totalDeleted = 0;

                // Cleanup orphaned markers for each tenant
                foreach (var tenant in tenants.Where(t => t.IsActive))
                {
                    var deleted = await markerService.CleanupOrphanedMarkersAsync(tenant.Id);
                    totalDeleted += deleted;
                }

                sw.Stop();
                if (totalDeleted > 0)
                {
                    _logger.LogInformation(
                        "Orphaned marker cleanup job completed in {ElapsedMs}ms. Deleted {TotalDeleted} orphaned markers across all tenants",
                        sw.ElapsedMilliseconds, totalDeleted);
                }
                else
                {
                    _logger.LogDebug("Orphaned marker cleanup job completed in {ElapsedMs}ms. No orphaned markers found",
                        sw.ElapsedMilliseconds);
                }

                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Error in orphaned marker cleanup service after {ElapsedMs}ms", sw.ElapsedMilliseconds);
                await Task.Delay(CleanupInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Orphaned Marker Cleanup Service stopped");
    }
}
