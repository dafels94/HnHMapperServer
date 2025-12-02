using HnHMapperServer.Web.Models;
using Microsoft.AspNetCore.Components;

namespace HnHMapperServer.Web.Components.Map.Sidebar;

public partial class RoadsPanel
{
    #region Parameters

    [Parameter] public IReadOnlyList<RoadViewModel> Roads { get; set; } = Array.Empty<RoadViewModel>();
    [Parameter] public IReadOnlyList<MapInfoModel> Maps { get; set; } = Array.Empty<MapInfoModel>();
    [Parameter] public EventCallback<RoadViewModel> OnRoadSelected { get; set; }
    [Parameter] public EventCallback<RoadViewModel> OnRoadEdit { get; set; }
    [Parameter] public EventCallback<RoadViewModel> OnRoadDelete { get; set; }

    #endregion

    #region State

    private string roadFilter = "";

    #endregion

    #region Filter

    private void OnFilterChanged(string value)
    {
        roadFilter = value;
        StateHasChanged();
    }

    private IEnumerable<RoadViewModel> GetFilteredRoads()
    {
        return Roads
            .Where(r => !r.Hidden &&
                       (string.IsNullOrWhiteSpace(roadFilter) ||
                        r.Name.Contains(roadFilter, StringComparison.OrdinalIgnoreCase) ||
                        r.CreatedBy.Contains(roadFilter, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.CreatedAt);
    }

    #endregion

    #region Helper Methods

    private string GetMapName(int mapId)
    {
        var map = Maps.FirstOrDefault(m => m.ID == mapId);
        return map?.MapInfo.Name ?? $"Map {mapId}";
    }

    #endregion
}
