using System.Diagnostics;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Api.BackgroundServices;

/// <summary>
/// Background service that expires old invitations and removes pending users after 7 days
/// Runs every hour
/// </summary>
public class InvitationExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InvitationExpirationService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public InvitationExpirationService(
        IServiceProvider serviceProvider,
        ILogger<InvitationExpirationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Randomized startup delay to prevent all services starting simultaneously
        var startupDelay = TimeSpan.FromSeconds(Random.Shared.Next(0, 60));
        _logger.LogInformation("Invitation Expiration Service starting in {Delay:F1}s", startupDelay.TotalSeconds);
        await Task.Delay(startupDelay, stoppingToken);

        _logger.LogInformation("Invitation Expiration Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogDebug("Invitation expiration job started");

                await ProcessExpiredInvitationsAsync();
                await RemovePendingUsersAfter7DaysAsync();

                sw.Stop();
                _logger.LogDebug("Invitation expiration job completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Error processing invitation expiration after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Invitation Expiration Service stopped");
    }

    private async Task ProcessExpiredInvitationsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var invitationRepository = scope.ServiceProvider.GetRequiredService<ITenantInvitationRepository>();

        var expiredInvitations = await invitationRepository.GetExpiredActiveInvitationsAsync();

        if (!expiredInvitations.Any())
        {
            return;
        }

        foreach (var invitation in expiredInvitations)
        {
            invitation.Status = "Expired";
            await invitationRepository.UpdateAsync(invitation);
        }

        _logger.LogInformation("Expired {Count} invitations", expiredInvitations.Count);
    }

    private async Task RemovePendingUsersAfter7DaysAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

        // Find pending registrations older than 7 days
        var oldPendingInvitations = await dbContext.TenantInvitations
            .IgnoreQueryFilters()
            .Where(i => i.Status == "Used" &&
                       i.PendingApproval &&
                       i.UsedAt.HasValue &&
                       i.UsedAt.Value < sevenDaysAgo)
            .ToListAsync();

        if (!oldPendingInvitations.Any())
        {
            return;
        }

        int deletedCount = 0;

        foreach (var invitation in oldPendingInvitations)
        {
            try
            {
                // Find and delete the pending TenantUser entry
                var tenantUser = await dbContext.TenantUsers
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(tu => tu.UserId == invitation.UsedBy && tu.TenantId == invitation.TenantId);

                if (tenantUser != null)
                {
                    dbContext.TenantUsers.Remove(tenantUser);
                }

                // Mark invitation as expired
                invitation.Status = "Expired";
                invitation.PendingApproval = false;

                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing pending user for invitation {InvitationId}", invitation.Id);
            }
        }

        await dbContext.SaveChangesAsync();

        if (deletedCount > 0)
        {
            _logger.LogInformation("Removed {Count} pending users older than 7 days", deletedCount);
        }
    }
}
