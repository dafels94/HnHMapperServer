using HnHMapperServer.Core.DTOs;
using HnHMapperServer.Web.Models;
using Microsoft.AspNetCore.Components;

namespace HnHMapperServer.Web.Components.Map.Sidebar;

public partial class MarkersPanel
{
    #region Parameters

    [Parameter] public IReadOnlyList<MarkerModel> Markers { get; set; } = Array.Empty<MarkerModel>();
    [Parameter] public IReadOnlyList<CustomMarkerViewModel> CustomMarkers { get; set; } = Array.Empty<CustomMarkerViewModel>();
    [Parameter] public IReadOnlyList<MapInfoModel> Maps { get; set; } = Array.Empty<MapInfoModel>();
    [Parameter] public IReadOnlyList<TimerDto> Timers { get; set; } = Array.Empty<TimerDto>();
    [Parameter] public EventCallback<MarkerModel> OnMarkerSelected { get; set; }
    [Parameter] public EventCallback<CustomMarkerViewModel> OnCustomMarkerEdit { get; set; }
    [Parameter] public EventCallback<CustomMarkerViewModel> OnCustomMarkerDelete { get; set; }
    [Parameter] public EventCallback<CustomMarkerViewModel> OnCustomMarkerSelected { get; set; }
    [Parameter] public EventCallback<MarkerModel> OnSetTimerForMarker { get; set; }
    [Parameter] public EventCallback<CustomMarkerViewModel> OnSetTimerForCustomMarker { get; set; }
    [Parameter] public HashSet<string> HiddenMarkerGroups { get; set; } = new();
    [Parameter] public EventCallback<(string ImageType, bool Visible)> OnMarkerGroupVisibilityChanged { get; set; }

    #endregion

    #region State

    // Lightweight group info for fast initial render (counts only)
    private class MarkerGroupInfo
    {
        public string ImageType { get; set; } = "";
        public int Count { get; set; }
    }

    // Cached group data - counts only, markers loaded via GetMarkersForGroup
    private List<MarkerGroupInfo> cachedGroups = new();
    private int totalFilteredCount = 0;

    // Track which groups are expanded (for deferred marker loading)
    private HashSet<string> expandedGroups = new();

    // Cache invalidation tracking
    private string lastMarkerFilter = "";
    private int lastMarkerCount = 0;

    // O(1) timer lookups
    private Dictionary<int, TimerDto> markerTimerLookup = new();
    private Dictionary<int, TimerDto> customMarkerTimerLookup = new();

    #endregion

    #region Filter

    private string markerFilter = "";

    private void OnFilterChanged(string value)
    {
        markerFilter = value;
        expandedGroups.Clear(); // Close all groups when filter changes
        UpdateGroupCache();
        StateHasChanged();
    }

    #endregion

    #region Lifecycle

    protected override void OnParametersSet()
    {
        UpdateGroupCache();
        UpdateTimerLookups();
    }

    private void UpdateGroupCache()
    {
        // Skip if nothing changed
        if (markerFilter == lastMarkerFilter && Markers.Count == lastMarkerCount && cachedGroups.Count > 0)
            return;

        lastMarkerFilter = markerFilter;
        lastMarkerCount = Markers.Count;

        // Compute filtered markers once
        var filtered = Markers
            .Where(m => !m.Hidden &&
                       (string.IsNullOrWhiteSpace(markerFilter) ||
                        m.Name.Contains(markerFilter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        totalFilteredCount = filtered.Count;

        // Group and compute counts only
        cachedGroups = filtered
            .GroupBy(m => m.Image)
            .Select(g => new MarkerGroupInfo
            {
                ImageType = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.ImageType)
            .ToList();
    }

    private void UpdateTimerLookups()
    {
        // Build O(1) lookup dictionaries
        markerTimerLookup = Timers?
            .Where(t => t.Type == "Marker" && !t.IsCompleted && t.MarkerId.HasValue)
            .GroupBy(t => t.MarkerId!.Value)
            .ToDictionary(g => g.Key, g => g.First()) ?? new();

        customMarkerTimerLookup = Timers?
            .Where(t => t.Type == "CustomMarker" && !t.IsCompleted && t.CustomMarkerId.HasValue)
            .GroupBy(t => t.CustomMarkerId!.Value)
            .ToDictionary(g => g.Key, g => g.First()) ?? new();
    }

    #endregion

    #region Marker Data

    /// <summary>
    /// Check if a group is expanded.
    /// </summary>
    private bool IsGroupExpanded(string imageType) => expandedGroups.Contains(imageType);

    /// <summary>
    /// Toggle group expansion state.
    /// </summary>
    private void ToggleGroup(string imageType)
    {
        if (expandedGroups.Contains(imageType))
            expandedGroups.Remove(imageType);
        else
            expandedGroups.Add(imageType);
    }

    /// <summary>
    /// Get markers for a specific group - called from MudVirtualize in template.
    /// Returns empty if group not expanded to avoid expensive LINQ on collapsed groups.
    /// </summary>
    private ICollection<MarkerModel> GetMarkersForGroup(string imageType)
    {
        // Return empty if group not expanded - prevents expensive LINQ on collapsed groups
        if (!expandedGroups.Contains(imageType))
            return Array.Empty<MarkerModel>();

        return Markers
            .Where(m => !m.Hidden &&
                       m.Image == imageType &&
                       (string.IsNullOrWhiteSpace(markerFilter) ||
                        m.Name.Contains(markerFilter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.Name)
            .ToList();
    }

    #endregion

    #region Filtered Properties (for custom markers only - they're fewer)

    private IEnumerable<CustomMarkerViewModel> GetFilteredCustomMarkers()
    {
        return CustomMarkers
            .Where(m => !m.Hidden &&
                       (string.IsNullOrWhiteSpace(markerFilter) ||
                        m.Title.Contains(markerFilter, StringComparison.OrdinalIgnoreCase) ||
                        (m.Description?.Contains(markerFilter, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderByDescending(m => m.PlacedAt);
    }

    #endregion

    #region Helper Methods

    private string GetMapName(int mapId)
    {
        var map = Maps.FirstOrDefault(m => m.ID == mapId);
        return map?.MapInfo.Name ?? $"Map {mapId}";
    }

    private static string GetIconSrc(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return "/gfx/terobjs/mm/custom.png";

        var trimmed = icon.Trim();
        return trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
    }

    private static string GetMarkerIconSrc(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return "/gfx/terobjs/mm/custom.png";

        var trimmed = imagePath.Trim();
        var withSlash = trimmed.StartsWith('/') ? trimmed : $"/{trimmed}";
        return withSlash.EndsWith(".png") ? withSlash : $"{withSlash}.png";
    }

    private TimerDto? GetTimerForMarker(int markerId)
        => markerTimerLookup.TryGetValue(markerId, out var t) ? t : null;

    private TimerDto? GetTimerForCustomMarker(int customMarkerId)
        => customMarkerTimerLookup.TryGetValue(customMarkerId, out var t) ? t : null;

    private static string GetIconDisplayName(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return "Unknown";

        var lastSlash = imagePath.LastIndexOf('/');
        var name = lastSlash >= 0 ? imagePath.Substring(lastSlash + 1) : imagePath;

        if (name.Length > 0)
            return char.ToUpperInvariant(name[0]) + name.Substring(1);

        return name;
    }

    private bool IsGroupVisible(string imageType) => !HiddenMarkerGroups.Contains(imageType);

    private async Task ToggleGroupVisibility(string imageType)
    {
        var isCurrentlyVisible = IsGroupVisible(imageType);
        await OnMarkerGroupVisibilityChanged.InvokeAsync((imageType, !isCurrentlyVisible));
    }

    #endregion
}
