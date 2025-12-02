using System.Diagnostics;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Services.Interfaces;

namespace HnHMapperServer.Api.BackgroundServices;

/// <summary>
/// Background service that auto-deletes maps that have remained empty for longer than a configured threshold
/// Default behavior: deletes maps with no grids/tiles that are older than 1 hour
/// Includes hidden maps in cleanup
/// </summary>
public class MapCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MapCleanupService> _logger;

    public MapCleanupService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<MapCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Randomized startup delay to prevent all services starting simultaneously
        var startupDelay = TimeSpan.FromSeconds(Random.Shared.Next(0, 60));
        _logger.LogInformation("Map Cleanup Service starting in {Delay:F1}s", startupDelay.TotalSeconds);
        await Task.Delay(startupDelay, stoppingToken);

        // Read configuration with defaults
        var deleteAfterMinutes = _configuration.GetValue<int>("Cleanup:DeleteEmptyMapsAfterMinutes", 60);
        var cleanupIntervalSeconds = _configuration.GetValue<int>("Cleanup:MapCleanupIntervalSeconds", 600);

        _logger.LogInformation(
            "Map Cleanup Service started - will delete empty maps older than {DeleteAfterMinutes} minutes, checking every {CleanupIntervalSeconds} seconds",
            deleteAfterMinutes,
            cleanupIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(cleanupIntervalSeconds), stoppingToken);

                var sw = Stopwatch.StartNew();
                _logger.LogInformation("Map cleanup job started");

                using var scope = _scopeFactory.CreateScope();
                var mapRepository = scope.ServiceProvider.GetRequiredService<IMapRepository>();
                var updateNotificationService = scope.ServiceProvider.GetRequiredService<IUpdateNotificationService>();
                var gridStorage = _configuration["GridStorage"] ?? "map";

                // Calculate cutoff time
                var cutoffUtc = DateTime.UtcNow.AddMinutes(-deleteAfterMinutes);

                _logger.LogDebug("Map cleanup check starting (cutoff: {Cutoff:yyyy-MM-dd HH:mm:ss} UTC)", cutoffUtc);

                // Find empty maps older than cutoff
                var emptyMapIds = await mapRepository.GetEmptyMapIdsCreatedBeforeAsync(cutoffUtc);

                _logger.LogDebug("Map cleanup check found {Count} empty map(s) to delete", emptyMapIds.Count);

                if (emptyMapIds.Count > 0)
                {
                    _logger.LogInformation("Found {Count} empty map(s) to delete (created before {Cutoff:yyyy-MM-dd HH:mm:ss} UTC)",
                        emptyMapIds.Count, cutoffUtc);

                    foreach (var mapId in emptyMapIds)
                    {
                        try
                        {
                            // Delete map from database
                            await mapRepository.DeleteMapAsync(mapId);
                            _logger.LogInformation("Deleted empty map {MapId}", mapId);

                            // Notify SSE clients that the map was deleted
                            updateNotificationService.NotifyMapDeleted(mapId);

                            // Attempt to delete the map's folder if it exists and is empty
                            var mapFolderPath = Path.Combine(gridStorage, mapId.ToString());
                            if (Directory.Exists(mapFolderPath))
                            {
                                try
                                {
                                    // Only delete if directory is empty
                                    if (!Directory.EnumerateFileSystemEntries(mapFolderPath).Any())
                                    {
                                        Directory.Delete(mapFolderPath, recursive: false);
                                        _logger.LogDebug("Deleted empty folder for map {MapId}", mapId);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Folder for map {MapId} is not empty, skipping filesystem cleanup", mapId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Log but don't fail - filesystem cleanup is best-effort
                                    _logger.LogWarning(ex, "Failed to delete folder for map {MapId}, ignoring", mapId);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting map {MapId}", mapId);
                        }
                    }
                }

                sw.Stop();
                _logger.LogInformation("Map cleanup job completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in map cleanup service");
                // Continue running despite error
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }

        _logger.LogInformation("Map Cleanup Service stopped");
    }
}

