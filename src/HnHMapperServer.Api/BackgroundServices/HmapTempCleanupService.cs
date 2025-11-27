namespace HnHMapperServer.Api.BackgroundServices;

/// <summary>
/// Background service that cleans up temporary .hmap files older than 7 days.
/// Temp files are created during import and kept for debugging failed imports.
/// Runs once per day to avoid unnecessary disk checks.
/// </summary>
public class HmapTempCleanupService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<HmapTempCleanupService> _logger;

    // Default: 7 days retention, check once per day
    private const int DefaultRetentionDays = 7;
    private const int DefaultCleanupIntervalHours = 24;

    public HmapTempCleanupService(
        IConfiguration configuration,
        ILogger<HmapTempCleanupService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = _configuration.GetValue<int>("Cleanup:HmapTempRetentionDays", DefaultRetentionDays);
        var cleanupIntervalHours = _configuration.GetValue<int>("Cleanup:HmapTempCleanupIntervalHours", DefaultCleanupIntervalHours);

        _logger.LogInformation(
            "HMAP Temp Cleanup Service started - will delete temp files older than {RetentionDays} days, checking every {CleanupIntervalHours} hours",
            retentionDays,
            cleanupIntervalHours);

        // Run cleanup on startup
        await CleanupTempFilesAsync(retentionDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(cleanupIntervalHours), stoppingToken);
                await CleanupTempFilesAsync(retentionDays);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HMAP temp cleanup service");
                // Continue running despite error
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        _logger.LogInformation("HMAP Temp Cleanup Service stopped");
    }

    private Task CleanupTempFilesAsync(int retentionDays)
    {
        try
        {
            var gridStorage = _configuration["GridStorage"] ?? "map";
            var tempDir = Path.Combine(gridStorage, "hmap-temp");

            if (!Directory.Exists(tempDir))
            {
                _logger.LogDebug("HMAP temp directory does not exist, nothing to clean up");
                return Task.CompletedTask;
            }

            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var deletedCount = 0;
            var totalSize = 0L;

            foreach (var filePath in Directory.GetFiles(tempDir, "*.hmap"))
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.LastWriteTimeUtc < cutoffDate)
                    {
                        totalSize += fileInfo.Length;
                        File.Delete(filePath);
                        deletedCount++;
                        _logger.LogDebug("Deleted stale temp file: {FilePath}", filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file: {FilePath}", filePath);
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "HMAP temp cleanup: deleted {Count} file(s), freed {Size:F2} MB",
                    deletedCount,
                    totalSize / (1024.0 * 1024.0));
            }
            else
            {
                _logger.LogDebug("HMAP temp cleanup: no stale files found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during HMAP temp file cleanup");
        }

        return Task.CompletedTask;
    }
}
