using System.Collections.Concurrent;

namespace HnHMapperServer.Services.Services;

/// <summary>
/// Service to manage import locks and cooldowns per tenant.
/// Prevents concurrent imports and enforces cooldown between imports.
/// </summary>
public class ImportLockService
{
    private readonly ConcurrentDictionary<string, ImportState> _importStates = new();
    private readonly TimeSpan _cooldownDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Attempt to acquire an import lock for a tenant.
    /// </summary>
    /// <returns>True if lock acquired, false if already locked or in cooldown</returns>
    public (bool Success, string? Reason, TimeSpan? WaitTime) TryAcquireLock(string tenantId)
    {
        var state = _importStates.GetOrAdd(tenantId, _ => new ImportState());

        lock (state)
        {
            // Check if already importing
            if (state.IsImporting)
            {
                return (false, "An import is already in progress for this tenant.", null);
            }

            // Check cooldown
            if (state.LastCompletedAt.HasValue)
            {
                var elapsed = DateTime.UtcNow - state.LastCompletedAt.Value;
                if (elapsed < _cooldownDuration)
                {
                    var remaining = _cooldownDuration - elapsed;
                    return (false, $"Please wait {remaining.Minutes}m {remaining.Seconds}s before starting another import.", remaining);
                }
            }

            // Acquire lock
            state.IsImporting = true;
            state.StartedAt = DateTime.UtcNow;
            state.CreatedMapIds.Clear();
            state.CreatedGridIds.Clear();
            return (true, null, null);
        }
    }

    /// <summary>
    /// Release the import lock after successful completion.
    /// </summary>
    public void ReleaseLock(string tenantId, bool success)
    {
        if (_importStates.TryGetValue(tenantId, out var state))
        {
            lock (state)
            {
                state.IsImporting = false;
                state.LastCompletedAt = DateTime.UtcNow;
                state.LastWasSuccessful = success;

                // Clear tracking on success (cleanup not needed)
                if (success)
                {
                    state.CreatedMapIds.Clear();
                    state.CreatedGridIds.Clear();
                }
            }
        }
    }

    /// <summary>
    /// Track a created map for potential cleanup.
    /// </summary>
    public void TrackCreatedMap(string tenantId, int mapId)
    {
        if (_importStates.TryGetValue(tenantId, out var state))
        {
            lock (state)
            {
                state.CreatedMapIds.Add(mapId);
            }
        }
    }

    /// <summary>
    /// Track a created grid for potential cleanup.
    /// </summary>
    public void TrackCreatedGrid(string tenantId, string gridId)
    {
        if (_importStates.TryGetValue(tenantId, out var state))
        {
            lock (state)
            {
                state.CreatedGridIds.Add(gridId);
            }
        }
    }

    /// <summary>
    /// Get items to clean up after a failed import.
    /// </summary>
    public (List<int> MapIds, List<string> GridIds) GetItemsToCleanup(string tenantId)
    {
        if (_importStates.TryGetValue(tenantId, out var state))
        {
            lock (state)
            {
                return (state.CreatedMapIds.ToList(), state.CreatedGridIds.ToList());
            }
        }
        return (new List<int>(), new List<string>());
    }

    /// <summary>
    /// Clear tracked items after cleanup.
    /// </summary>
    public void ClearTrackedItems(string tenantId)
    {
        if (_importStates.TryGetValue(tenantId, out var state))
        {
            lock (state)
            {
                state.CreatedMapIds.Clear();
                state.CreatedGridIds.Clear();
            }
        }
    }

    /// <summary>
    /// Get import status for a tenant.
    /// </summary>
    public ImportStatusDto GetStatus(string tenantId)
    {
        if (_importStates.TryGetValue(tenantId, out var state))
        {
            lock (state)
            {
                TimeSpan? cooldownRemaining = null;
                if (!state.IsImporting && state.LastCompletedAt.HasValue)
                {
                    var elapsed = DateTime.UtcNow - state.LastCompletedAt.Value;
                    if (elapsed < _cooldownDuration)
                    {
                        cooldownRemaining = _cooldownDuration - elapsed;
                    }
                }

                return new ImportStatusDto
                {
                    IsImporting = state.IsImporting,
                    StartedAt = state.StartedAt,
                    LastCompletedAt = state.LastCompletedAt,
                    CooldownRemaining = cooldownRemaining,
                    CanImport = !state.IsImporting && !cooldownRemaining.HasValue
                };
            }
        }

        return new ImportStatusDto { CanImport = true };
    }

    private class ImportState
    {
        public bool IsImporting { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? LastCompletedAt { get; set; }
        public bool LastWasSuccessful { get; set; }
        public HashSet<int> CreatedMapIds { get; } = new();
        public HashSet<string> CreatedGridIds { get; } = new();
    }
}

/// <summary>
/// DTO for import status.
/// </summary>
public class ImportStatusDto
{
    public bool IsImporting { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? LastCompletedAt { get; set; }
    public TimeSpan? CooldownRemaining { get; set; }
    public bool CanImport { get; set; }
}
