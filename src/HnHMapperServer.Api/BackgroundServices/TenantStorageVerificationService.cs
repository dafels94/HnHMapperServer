using System.Diagnostics;
using HnHMapperServer.Infrastructure.Data;
using HnHMapperServer.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Api.BackgroundServices;

/// <summary>
/// Background service that periodically verifies tenant storage usage matches filesystem reality
/// Runs every 6 hours to detect and fix discrepancies between database and actual file storage
/// </summary>
public class TenantStorageVerificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantStorageVerificationService> _logger;
    private readonly string _gridStorage;
    private readonly TimeSpan _verificationInterval;

    public TenantStorageVerificationService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<TenantStorageVerificationService> _logger)
    {
        _serviceProvider = serviceProvider;
        this._logger = _logger;
        _gridStorage = configuration["GridStorage"] ?? "map";

        // Default: 6 hours (configurable)
        var intervalHours = configuration.GetValue<int>("StorageVerification:IntervalHours", 6);
        _verificationInterval = TimeSpan.FromHours(intervalHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Randomized startup delay to prevent all services starting simultaneously
        var startupDelay = TimeSpan.FromSeconds(Random.Shared.Next(0, 60));
        _logger.LogInformation("TenantStorageVerificationService starting in {Delay:F1}s", startupDelay.TotalSeconds);
        await Task.Delay(startupDelay, stoppingToken);

        _logger.LogInformation("TenantStorageVerificationService started. Interval: {Interval}", _verificationInterval);

        // Wait 1 hour before first run (let system stabilize after startup)
        await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerifyAllTenantsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during storage verification");
            }

            // Wait for next verification cycle
            await Task.Delay(_verificationInterval, stoppingToken);
        }

        _logger.LogInformation("TenantStorageVerificationService stopped");
    }

    private async Task VerifyAllTenantsAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Storage verification job started");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var quotaService = scope.ServiceProvider.GetRequiredService<IStorageQuotaService>();

        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Starting storage verification for {Count} active tenants", tenants.Count);

        foreach (var tenant in tenants)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await VerifyTenantAsync(tenant.Id, db, quotaService);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify storage for tenant {TenantId}", tenant.Id);
            }
        }

        sw.Stop();
        _logger.LogInformation("Storage verification job completed in {ElapsedMs}ms for {Count} tenants", sw.ElapsedMilliseconds, tenants.Count);
    }

    private async Task VerifyTenantAsync(string tenantId, ApplicationDbContext db, IStorageQuotaService quotaService)
    {
        // Calculate from filesystem only (avoids expensive DB query that causes lockups)
        var tenantDir = Path.Combine(_gridStorage, "tenants", tenantId);
        var fsUsageBytes = CalculateDirectorySize(tenantDir);
        var fsUsageMB = fsUsageBytes / 1024.0 / 1024.0;

        // Get current tracked value
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant != null)
        {
            var oldUsage = tenant.CurrentStorageMB;
            var diffMB = Math.Abs(oldUsage - fsUsageMB);

            // Update if there's a significant difference (> 1 MB)
            if (diffMB > 1)
            {
                tenant.CurrentStorageMB = fsUsageMB;
                await db.SaveChangesAsync();

                _logger.LogInformation(
                    "Updated tenant {TenantId} storage usage: {OldMB:F2}MB → {NewMB:F2}MB",
                    tenantId, oldUsage, fsUsageMB);
            }
            else
            {
                _logger.LogDebug(
                    "Storage verified for tenant {TenantId}: {UsageMB:F2}MB",
                    tenantId, fsUsageMB);
            }
        }

        // Recalculate storage (this also writes .storage.json metadata file)
        await quotaService.RecalculateStorageUsageAsync(tenantId, _gridStorage);
    }

    private static long CalculateDirectorySize(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            return 0;

        var dirInfo = new DirectoryInfo(dirPath);

        try
        {
            return dirInfo.GetFiles("*", SearchOption.AllDirectories)
                .Sum(fi => fi.Length);
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
            return 0;
        }
        catch (DirectoryNotFoundException)
        {
            // Directory was deleted during scan
            return 0;
        }
    }
}
