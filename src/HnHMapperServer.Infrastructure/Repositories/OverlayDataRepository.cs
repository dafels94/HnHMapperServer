using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Core.Models;
using HnHMapperServer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Infrastructure.Repositories;

public class OverlayDataRepository : IOverlayDataRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextAccessor _tenantContext;

    public OverlayDataRepository(ApplicationDbContext context, ITenantContextAccessor tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<List<OverlayData>> GetOverlaysForGridAsync(int mapId, int x, int y)
    {
        var currentTenantId = _tenantContext.GetCurrentTenantId();

        var query = _context.OverlayData.AsNoTracking();

        if (!string.IsNullOrEmpty(currentTenantId))
        {
            query = query.Where(o => o.TenantId == currentTenantId);
        }

        var entities = await query
            .Where(o => o.MapId == mapId && o.CoordX == x && o.CoordY == y)
            .ToListAsync();

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<List<OverlayData>> GetOverlaysForGridsAsync(int mapId, IEnumerable<(int X, int Y)> coords)
    {
        var currentTenantId = _tenantContext.GetCurrentTenantId();
        var coordList = coords.ToList();

        if (coordList.Count == 0)
            return new List<OverlayData>();

        // Query Strategy:
        // Use rectangular bounds query (X IN (...) AND Y IN (...)) which is efficient
        // with the covering index (TenantId, MapId, CoordX, CoordY).
        // Post-filter in memory for exact coordinate matches.
        // This approach is optimal for typical use cases where tiles are spatially close,
        // and the covering index ensures fast index-only lookups.
        var xCoords = coordList.Select(c => c.X).Distinct().ToList();
        var yCoords = coordList.Select(c => c.Y).Distinct().ToList();

        var query = _context.OverlayData.AsNoTracking();

        if (!string.IsNullOrEmpty(currentTenantId))
        {
            query = query.Where(o => o.TenantId == currentTenantId);
        }

        var entities = await query
            .Where(o => o.MapId == mapId && xCoords.Contains(o.CoordX) && yCoords.Contains(o.CoordY))
            .ToListAsync();

        // Post-filter to exact coordinate matches (handles sparse diagonal requests)
        var coordSet = new HashSet<(int, int)>(coordList);
        return entities
            .Where(e => coordSet.Contains((e.CoordX, e.CoordY)))
            .Select(MapToDomain)
            .ToList();
    }

    public async Task<List<string>> GetOverlayTypesForMapAsync(int mapId)
    {
        var currentTenantId = _tenantContext.GetCurrentTenantId();

        var query = _context.OverlayData.AsNoTracking();

        if (!string.IsNullOrEmpty(currentTenantId))
        {
            query = query.Where(o => o.TenantId == currentTenantId);
        }

        return await query
            .Where(o => o.MapId == mapId)
            .Select(o => o.OverlayType)
            .Distinct()
            .ToListAsync();
    }

    public async Task UpsertBatchAsync(IEnumerable<OverlayData> overlays)
    {
        var overlayList = overlays.ToList();
        if (overlayList.Count == 0) return;

        const int chunkSize = 500;

        foreach (var chunk in overlayList.Chunk(chunkSize))
        {
            // Get existing overlays for this chunk
            var xCoords = chunk.Select(o => o.Coord.X).Distinct().ToList();
            var yCoords = chunk.Select(o => o.Coord.Y).Distinct().ToList();
            var mapIds = chunk.Select(o => o.MapId).Distinct().ToList();
            var tenantIds = chunk.Select(o => o.TenantId).Distinct().ToList();

            var existing = await _context.OverlayData
                .IgnoreQueryFilters()
                .Where(o => mapIds.Contains(o.MapId)
                         && xCoords.Contains(o.CoordX)
                         && yCoords.Contains(o.CoordY)
                         && tenantIds.Contains(o.TenantId))
                .ToDictionaryAsync(o => (o.MapId, o.CoordX, o.CoordY, o.OverlayType, o.TenantId));

            foreach (var overlay in chunk)
            {
                var key = (overlay.MapId, overlay.Coord.X, overlay.Coord.Y, overlay.OverlayType, overlay.TenantId);

                if (existing.TryGetValue(key, out var existingEntity))
                {
                    // Update existing
                    existingEntity.Data = overlay.Data;
                    existingEntity.UpdatedAt = overlay.UpdatedAt;
                }
                else
                {
                    // Insert new
                    _context.OverlayData.Add(MapFromDomain(overlay));
                }
            }

            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
        }
    }

    public async Task DeleteByMapAsync(int mapId)
    {
        var currentTenantId = _tenantContext.GetCurrentTenantId();

        var query = _context.OverlayData.Where(o => o.MapId == mapId);

        if (!string.IsNullOrEmpty(currentTenantId))
        {
            query = query.Where(o => o.TenantId == currentTenantId);
        }

        var overlays = await query.ToListAsync();
        _context.OverlayData.RemoveRange(overlays);
        await _context.SaveChangesAsync();
    }

    private static OverlayData MapToDomain(OverlayDataEntity entity) => new OverlayData
    {
        Id = entity.Id,
        MapId = entity.MapId,
        Coord = new Coord(entity.CoordX, entity.CoordY),
        OverlayType = entity.OverlayType,
        Data = entity.Data,
        TenantId = entity.TenantId,
        UpdatedAt = entity.UpdatedAt
    };

    private static OverlayDataEntity MapFromDomain(OverlayData overlay) => new OverlayDataEntity
    {
        MapId = overlay.MapId,
        CoordX = overlay.Coord.X,
        CoordY = overlay.Coord.Y,
        OverlayType = overlay.OverlayType,
        Data = overlay.Data,
        TenantId = overlay.TenantId,
        UpdatedAt = overlay.UpdatedAt
    };
}
