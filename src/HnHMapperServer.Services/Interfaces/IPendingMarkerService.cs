namespace HnHMapperServer.Services.Interfaces;

/// <summary>
/// Service for managing markers that are uploaded before their grids exist.
/// Queues markers in memory and saves them when the grid arrives.
/// </summary>
public interface IPendingMarkerService
{
    /// <summary>
    /// Queue a marker for later processing when its grid arrives.
    /// </summary>
    void QueueMarker(string tenantId, string gridId, int x, int y, string name, string image);

    /// <summary>
    /// Process all pending markers for a grid that was just uploaded.
    /// Saves them to the database and removes them from the queue.
    /// </summary>
    /// <returns>Number of markers saved</returns>
    Task<int> ProcessPendingMarkersForGridAsync(string tenantId, string gridId);

    /// <summary>
    /// Remove expired pending markers (older than 1 hour).
    /// Called periodically to prevent memory leaks.
    /// </summary>
    /// <returns>Number of markers removed</returns>
    int CleanupExpiredPendingMarkers();

    /// <summary>
    /// Get the count of pending markers (for diagnostics).
    /// </summary>
    int GetPendingCount();
}
