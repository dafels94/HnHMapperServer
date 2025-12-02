using System.Diagnostics;
using HnHMapperServer.Services.Interfaces;

namespace HnHMapperServer.Api.BackgroundServices;

/// <summary>
/// Background service that cleans up old map preview images.
/// Deletes preview images older than 7 days.
/// Runs every 6 hours.
/// </summary>
public class PreviewCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PreviewCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    public PreviewCleanupService(
        IServiceProvider serviceProvider,
        ILogger<PreviewCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Randomized startup delay to prevent all services starting simultaneously
        var startupDelay = TimeSpan.FromSeconds(Random.Shared.Next(0, 60));
        _logger.LogInformation("Preview Cleanup Service starting in {Delay:F1}s", startupDelay.TotalSeconds);
        await Task.Delay(startupDelay, stoppingToken);

        _logger.LogInformation("Preview Cleanup Service started (runs every 6 hours)");

        // Run immediately on startup, then every 6 hours
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOldPreviewsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during preview cleanup");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Service is stopping
                break;
            }
        }

        _logger.LogInformation("Preview Cleanup Service stopped");
    }

    private async Task CleanupOldPreviewsAsync()
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Preview cleanup job started");

        using var scope = _serviceProvider.CreateScope();
        var mapPreviewService = scope.ServiceProvider.GetRequiredService<IMapPreviewService>();

        try
        {
            var deletedCount = await mapPreviewService.CleanupOldPreviewsAsync();

            sw.Stop();
            if (deletedCount > 0)
            {
                _logger.LogInformation("Preview cleanup job completed in {ElapsedMs}ms: deleted {Count} old preview images", sw.ElapsedMilliseconds, deletedCount);
            }
            else
            {
                _logger.LogInformation("Preview cleanup job completed in {ElapsedMs}ms: no old previews to delete", sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error cleaning up old preview images after {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
    }
}
