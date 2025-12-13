using HnHMapperServer.Core.Models;

namespace HnHMapperServer.Core.Interfaces;

public interface IMapRepository
{
    Task<MapInfo?> GetMapAsync(int mapId);
    Task<List<MapInfo>> GetAllMapsAsync();
    Task SaveMapAsync(MapInfo mapInfo);
    Task DeleteMapAsync(int mapId);
    Task<int> GetNextMapIdAsync();
    
    /// <summary>
    /// Gets IDs of maps that were created before the cutoff time and have fewer than the specified minimum tiles
    /// Used by MapCleanupService to auto-delete small/empty maps
    /// </summary>
    Task<List<int>> GetSmallMapIdsCreatedBeforeAsync(DateTime cutoffUtc, int minimumTileCount);
}
