using HnHMapperServer.Web.Models;

namespace HnHMapperServer.Web.Services.Map;

/// <summary>
/// Service for managing marker state and filtering
/// </summary>
public class MarkerStateService
{
    private readonly ILogger<MarkerStateService> _logger;

    /// <summary>
    /// List of all markers (across all maps)
    /// </summary>
    private List<MarkerModel> _allMarkers = new();

    /// <summary>
    /// Filter text for markers list
    /// </summary>
    private string _markerFilter = "";

    public MarkerStateService(ILogger<MarkerStateService> logger)
    {
        _logger = logger;
    }

    #region Public Properties

    /// <summary>
    /// All markers (read-only)
    /// </summary>
    public IReadOnlyList<MarkerModel> AllMarkers => _allMarkers.AsReadOnly();

    /// <summary>
    /// Marker filter text
    /// </summary>
    public string MarkerFilter
    {
        get => _markerFilter;
        set => _markerFilter = value;
    }

    /// <summary>
    /// Filtered list of markers based on current filter text (excludes hidden markers)
    /// </summary>
    public IEnumerable<MarkerModel> FilteredMarkers =>
        _allMarkers
            .Where(m => !m.Hidden &&
                       (string.IsNullOrWhiteSpace(_markerFilter) ||
                        m.Name.Contains(_markerFilter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.Name);

    #endregion

    #region State Management

    /// <summary>
    /// Replace all markers with new list (typically from API fetch)
    /// </summary>
    public void SetMarkers(List<MarkerModel> markers)
    {
        _allMarkers = markers;
        _logger.LogDebug("Markers updated: {Count} total markers", markers.Count);
    }

    /// <summary>
    /// Get all markers for a specific map (excludes hidden)
    /// </summary>
    public IEnumerable<MarkerModel> GetMarkersForMap(int mapId)
    {
        return _allMarkers.Where(m => m.Map == mapId && !m.Hidden);
    }

    /// <summary>
    /// Get marker by ID
    /// </summary>
    public MarkerModel? GetMarkerById(int markerId)
    {
        return _allMarkers.FirstOrDefault(m => m.Id == markerId);
    }

    /// <summary>
    /// Add or update a marker
    /// </summary>
    public void AddOrUpdateMarker(MarkerModel marker)
    {
        var existingIndex = _allMarkers.FindIndex(m => m.Id == marker.Id);
        if (existingIndex >= 0)
        {
            _allMarkers[existingIndex] = marker;
            _logger.LogDebug("Marker updated: Id={Id}, Name={Name}", marker.Id, marker.Name);
        }
        else
        {
            _allMarkers.Add(marker);
            _logger.LogDebug("Marker added: Id={Id}, Name={Name}", marker.Id, marker.Name);
        }
    }

    /// <summary>
    /// Remove marker by ID
    /// </summary>
    public bool RemoveMarker(int markerId)
    {
        var marker = _allMarkers.FirstOrDefault(m => m.Id == markerId);
        if (marker != null)
        {
            _allMarkers.Remove(marker);
            _logger.LogDebug("Marker removed: Id={Id}, Name={Name}", markerId, marker.Name);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Update markers from API response (adds new, updates existing, removes missing)
    /// Preserves markers from other maps - only modifies markers for the current map
    /// </summary>
    public MarkerUpdateResult UpdateFromApi(List<MarkerModel> apiMarkers, int currentMapId)
    {
        var result = new MarkerUpdateResult();

        // Get API markers for all maps (for global updates)
        var apiMarkerIds = apiMarkers.Select(m => m.Id).ToHashSet();

        // Update existing and add new markers across ALL maps
        foreach (var marker in apiMarkers.Where(m => !m.Hidden))
        {
            var existingIndex = _allMarkers.FindIndex(m => m.Id == marker.Id);
            if (existingIndex >= 0)
            {
                // Update existing marker
                _allMarkers[existingIndex] = marker;
                // Only track visual updates for current map
                if (marker.Map == currentMapId)
                {
                    result.UpdatedMarkers.Add(marker);
                }
            }
            else
            {
                // Add new marker
                _allMarkers.Add(marker);
                // Only track visual updates for current map
                if (marker.Map == currentMapId)
                {
                    result.AddedMarkers.Add(marker);
                }
            }
        }

        // Remove markers that are no longer in the API response (deleted on server)
        // This preserves markers from ALL maps, only removing truly deleted ones
        var toRemove = _allMarkers
            .Where(m => !apiMarkerIds.Contains(m.Id))
            .ToList();

        foreach (var marker in toRemove)
        {
            _allMarkers.Remove(marker);
            // Only track visual updates for current map
            if (marker.Map == currentMapId)
            {
                result.RemovedMarkerIds.Add(marker.Id);
            }
        }

        _logger.LogDebug("Marker update: {Added} added, {Updated} updated, {Removed} removed (for map {MapId})",
            result.AddedMarkers.Count, result.UpdatedMarkers.Count, result.RemovedMarkerIds.Count, currentMapId);

        return result;
    }

    /// <summary>
    /// Clear all markers
    /// </summary>
    public void Clear()
    {
        _allMarkers.Clear();
        _logger.LogDebug("All markers cleared");
    }

    #endregion

    #region Filtering and Queries

    /// <summary>
    /// Get markers by type
    /// </summary>
    public IEnumerable<MarkerModel> GetMarkersByType(string type)
    {
        return _allMarkers.Where(m => m.Type == type && !m.Hidden);
    }

    /// <summary>
    /// Get ready markers
    /// </summary>
    public IEnumerable<MarkerModel> GetReadyMarkers()
    {
        return _allMarkers.Where(m => m.Ready && !m.Hidden);
    }

    /// <summary>
    /// Count markers by map
    /// </summary>
    public int CountMarkersForMap(int mapId)
    {
        return _allMarkers.Count(m => m.Map == mapId && !m.Hidden);
    }

    #endregion
}

/// <summary>
/// Result of marker update operation
/// </summary>
public class MarkerUpdateResult
{
    public List<MarkerModel> AddedMarkers { get; } = new();
    public List<MarkerModel> UpdatedMarkers { get; } = new();
    public List<int> RemovedMarkerIds { get; } = new();
}
