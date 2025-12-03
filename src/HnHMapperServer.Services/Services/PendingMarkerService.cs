using System.Collections.Concurrent;
using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Core.Models;
using HnHMapperServer.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// In-memory queue for markers uploaded before their grids exist.
/// When a grid is uploaded, pending markers are saved to the database.
/// </summary>
public class PendingMarkerService : IPendingMarkerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingMarkerService> _logger;
    private readonly ConcurrentDictionary<string, ConcurrentBag<PendingMarker>> _pendingMarkers = new();
    private static readonly TimeSpan ExpirationTime = TimeSpan.FromHours(1);

    public PendingMarkerService(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingMarkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Queue a marker for later processing when its grid arrives.
    /// </summary>
    public void QueueMarker(string tenantId, string gridId, int x, int y, string name, string image)
    {
        var key = $"{tenantId}_{gridId}";
        var pending = new PendingMarker
        {
            TenantId = tenantId,
            GridId = gridId,
            X = x,
            Y = y,
            Name = name,
            Image = image,
            QueuedAt = DateTime.UtcNow
        };

        var bag = _pendingMarkers.GetOrAdd(key, _ => new ConcurrentBag<PendingMarker>());
        bag.Add(pending);

        _logger.LogDebug("Queued pending marker '{Name}' for grid {GridId} (tenant: {TenantId})",
            name, gridId, tenantId);
    }

    /// <summary>
    /// Process all pending markers for a grid that was just uploaded.
    /// </summary>
    public async Task<int> ProcessPendingMarkersForGridAsync(string tenantId, string gridId)
    {
        var key = $"{tenantId}_{gridId}";

        if (!_pendingMarkers.TryRemove(key, out var pendingBag) || pendingBag.IsEmpty)
            return 0;

        var markers = pendingBag.ToList();
        var saved = 0;

        using var scope = _scopeFactory.CreateScope();
        var markerRepository = scope.ServiceProvider.GetRequiredService<IMarkerRepository>();

        foreach (var pending in markers)
        {
            try
            {
                var markerKey = $"{pending.GridId}_{pending.X}_{pending.Y}";
                var marker = new Marker
                {
                    Id = 0,
                    Name = pending.Name,
                    GridId = pending.GridId,
                    Position = new Position(pending.X, pending.Y),
                    Image = pending.Image,
                    Ready = false,
                    MaxReady = -1,
                    MinReady = -1
                };

                await markerRepository.SaveMarkerAsync(marker, markerKey);
                saved++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save pending marker '{Name}' for grid {GridId}",
                    pending.Name, pending.GridId);
            }
        }

        if (saved > 0)
        {
            _logger.LogInformation("Saved {Count} pending markers for grid {GridId} (tenant: {TenantId})",
                saved, gridId, tenantId);
        }

        return saved;
    }

    /// <summary>
    /// Remove expired pending markers (older than 1 hour).
    /// </summary>
    public int CleanupExpiredPendingMarkers()
    {
        var cutoff = DateTime.UtcNow - ExpirationTime;
        var removed = 0;

        foreach (var kvp in _pendingMarkers)
        {
            var validMarkers = new ConcurrentBag<PendingMarker>();
            foreach (var marker in kvp.Value)
            {
                if (marker.QueuedAt >= cutoff)
                {
                    validMarkers.Add(marker);
                }
                else
                {
                    removed++;
                    _logger.LogDebug("Expired pending marker '{Name}' for grid {GridId}",
                        marker.Name, marker.GridId);
                }
            }

            // Replace the bag with only valid markers
            if (validMarkers.IsEmpty)
            {
                _pendingMarkers.TryRemove(kvp.Key, out _);
            }
            else
            {
                _pendingMarkers[kvp.Key] = validMarkers;
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired pending markers", removed);
        }

        return removed;
    }

    /// <summary>
    /// Get the count of pending markers (for diagnostics).
    /// </summary>
    public int GetPendingCount()
    {
        return _pendingMarkers.Values.Sum(bag => bag.Count);
    }

    private class PendingMarker
    {
        public required string TenantId { get; init; }
        public required string GridId { get; init; }
        public required int X { get; init; }
        public required int Y { get; init; }
        public required string Name { get; init; }
        public required string Image { get; init; }
        public required DateTime QueuedAt { get; init; }
    }
}
