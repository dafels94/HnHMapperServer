using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Core.Models;
using HnHMapperServer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Infrastructure.Repositories;

public class MapRepository : IMapRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextAccessor _tenantContext;

    public MapRepository(ApplicationDbContext context, ITenantContextAccessor tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<MapInfo?> GetMapAsync(int id)
    {
        // IMPORTANT: FindAsync bypasses tenant filters - use explicit tenant check
        var currentTenantId = _tenantContext.GetCurrentTenantId();
        if (string.IsNullOrEmpty(currentTenantId))
            return null;

        var entity = await _context.Maps
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == currentTenantId);
        return entity == null ? null : MapToDomain(entity);
    }

    public async Task<List<MapInfo>> GetAllMapsAsync()
    {
        var entities = await _context.Maps
            .AsNoTracking()
            .OrderBy(m => m.Priority)
            .ThenBy(m => m.Id)
            .ToListAsync();
        return entities.Select(MapToDomain).ToList();
    }

    public async Task SaveMapAsync(MapInfo map)
    {
        if (map.Id == 0)
        {
            var entity = MapFromDomain(map);
            _context.Maps.Add(entity);
            await _context.SaveChangesAsync();
            map.Id = entity.Id;
            return;
        }

        // IMPORTANT: FindAsync bypasses tenant filters - use explicit tenant check
        var currentTenantId = _tenantContext.GetRequiredTenantId();
        var existing = await _context.Maps
            .FirstOrDefaultAsync(m => m.Id == map.Id && m.TenantId == currentTenantId);

        if (existing is null)
        {
            var entity = MapFromDomain(map);
            _context.Maps.Add(entity);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(MapFromDomain(map));
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteMapAsync(int id)
    {
        // IMPORTANT: FindAsync bypasses tenant filters - use explicit tenant check
        var currentTenantId = _tenantContext.GetCurrentTenantId();
        if (string.IsNullOrEmpty(currentTenantId))
            return;

        var entity = await _context.Maps
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == currentTenantId);
        if (entity != null)
        {
            _context.Maps.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> GetNextMapIdAsync()
    {
        // IMPORTANT: Map IDs must be globally unique across all tenants
        // because Maps.Id is a PRIMARY KEY, not tenant-scoped
        var maxId = await _context.Maps
            .IgnoreQueryFilters()  // Get global max ID, not tenant-scoped
            .AsNoTracking()
            .MaxAsync(m => (int?)m.Id);
        return (maxId ?? 0) + 1;
    }

    public async Task<List<int>> GetSmallMapIdsCreatedBeforeAsync(DateTime cutoffUtc, int minimumTileCount)
    {
        // Find maps created before cutoff with fewer than the minimum number of tiles
        // Explicit tenant filtering for defense-in-depth to prevent cross-tenant data leakage
        var currentTenantId = _tenantContext.GetCurrentTenantId();

        var query = _context.Maps.AsNoTracking();

        // If tenant context is available, filter by tenant
        if (!string.IsNullOrEmpty(currentTenantId))
        {
            // Get all maps with their tile counts, with explicit tenant filtering on subqueries
            var allMaps = await query
                .Where(m => m.TenantId == currentTenantId)
                .Select(m => new
                {
                    m.Id,
                    m.CreatedAt,
                    TileCount = _context.Tiles.Count(t => t.MapId == m.Id && t.TenantId == currentTenantId)
                })
                .ToListAsync();

            // Filter in memory to find small maps (fewer than minimum tiles)
            return allMaps
                .Where(m => m.CreatedAt <= cutoffUtc && m.TileCount < minimumTileCount)
                .Select(m => m.Id)
                .ToList();
        }
        else
        {
            // No tenant context - return empty list to be safe
            return new List<int>();
        }
    }

    private static MapInfo MapToDomain(MapInfoEntity entity) => new MapInfo
    {
        Id = entity.Id,
        Name = entity.Name,
        Hidden = entity.Hidden,
        Priority = entity.Priority,
        CreatedAt = entity.CreatedAt
    };

    private MapInfoEntity MapFromDomain(MapInfo domain) => new MapInfoEntity
    {
        Id = domain.Id,
        Name = domain.Name,
        Hidden = domain.Hidden,
        Priority = domain.Priority,
        CreatedAt = domain.CreatedAt,
        TenantId = _tenantContext.GetRequiredTenantId()
    };
}
