using HnHMapperServer.Core.Models;

namespace HnHMapperServer.Core.Interfaces;

/// <summary>
/// Repository for overlay data (claims, villages, provinces)
/// </summary>
public interface IOverlayDataRepository
{
    /// <summary>
    /// Get all overlays for a specific grid coordinate
    /// </summary>
    Task<List<OverlayData>> GetOverlaysForGridAsync(int mapId, int x, int y);

    /// <summary>
    /// Get all overlays for multiple grid coordinates (bulk query)
    /// </summary>
    Task<List<OverlayData>> GetOverlaysForGridsAsync(int mapId, IEnumerable<(int X, int Y)> coords);

    /// <summary>
    /// Get all distinct overlay types present in a map
    /// </summary>
    Task<List<string>> GetOverlayTypesForMapAsync(int mapId);

    /// <summary>
    /// Upsert overlay data in batch (insert or update existing)
    /// </summary>
    Task UpsertBatchAsync(IEnumerable<OverlayData> overlays);

    /// <summary>
    /// Delete all overlays for a map
    /// </summary>
    Task DeleteByMapAsync(int mapId);
}
