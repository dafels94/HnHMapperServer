using HnHMapperServer.Services.Interfaces;

namespace HnHMapperServer.Api.BackgroundServices;

/// <summary>
/// Background service that automatically deletes stale food submissions
/// Runs every hour and deletes submissions older than 7 days
/// </summary>
public class FoodSubmissionCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FoodSubmissionCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);
    private const int DaysOld = 7;

    public FoodSubmissionCleanupService(
        IServiceProvider serviceProvider,
        ILogger<FoodSubmissionCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FoodSubmissionCleanupService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var foodSubmissionService = scope.ServiceProvider
                    .GetRequiredService<IFoodSubmissionService>();

                _logger.LogDebug("Running food submission cleanup");

                var deleted = await foodSubmissionService.DeleteStaleSubmissionsAsync(DaysOld, stoppingToken);

                if (deleted > 0)
                {
                    _logger.LogInformation("Deleted {Count} stale food submissions", deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during food submission cleanup");
            }
        }

        _logger.LogInformation("FoodSubmissionCleanupService stopped");
    }
}
