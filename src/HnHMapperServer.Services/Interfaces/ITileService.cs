using HnHMapperServer.Core.Models;

namespace HnHMapperServer.Services.Interfaces;

public interface ITileService
{
    /// <summary>
    /// Saves a tile to the repository and publishes update notification
    /// </summary>
    Task SaveTileAsync(int mapId, Coord coord, int zoom, string file, long timestamp, string tenantId, int fileSizeBytes);

    /// <summary>
    /// Gets a tile from the repository
    /// </summary>
    Task<TileData?> GetTileAsync(int mapId, Coord coord, int zoom);

    /// <summary>
    /// Updates the zoom level by combining 4 sub-tiles into one parent tile
    /// </summary>
    Task UpdateZoomLevelAsync(int mapId, Coord coord, int zoom, string tenantId, string gridStorage);

    /// <summary>
    /// Rebuilds all zoom levels for all tiles (admin operation)
    /// NOTE: Not yet updated for multi-tenancy
    /// </summary>
    Task RebuildZoomsAsync(string gridStorage);

    /// <summary>
    /// Rebuilds incomplete zoom tiles where new sub-tiles have been added since the zoom tile was created
    /// Returns the number of tiles rebuilt
    /// </summary>
    Task<int> RebuildIncompleteZoomTilesAsync(string tenantId, string gridStorage, int maxTilesToRebuild);
}
