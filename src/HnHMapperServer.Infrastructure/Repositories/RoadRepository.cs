using HnHMapperServer.Core.Interfaces;
using HnHMapperServer.Core.Models;
using HnHMapperServer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HnHMapperServer.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for road operations
/// </summary>
public class RoadRepository : IRoadRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextAccessor _tenantContext;

    public RoadRepository(ApplicationDbContext context, ITenantContextAccessor tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Get a road by ID
    /// </summary>
    public async Task<Road?> GetByIdAsync(int id)
    {
        // IMPORTANT: FindAsync bypasses tenant filters - use explicit tenant check
        var currentTenantId = _tenantContext.GetCurrentTenantId();
        if (string.IsNullOrEmpty(currentTenantId))
            return null;

        var entity = await _context.Roads
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == currentTenantId);
        return entity == null ? null : MapToDomain(entity);
    }

    /// <summary>
    /// Get all roads for a specific map (includes hidden roads)
    /// </summary>
    public async Task<List<Road>> GetByMapIdAsync(int mapId)
    {
        return await _context.Roads
            .AsNoTracking()
            .Where(r => r.MapId == mapId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new Road
            {
                Id = r.Id,
                MapId = r.MapId,
                Name = r.Name,
                Waypoints = r.Waypoints,
                CreatedBy = r.CreatedBy,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                Hidden = r.Hidden
            })
            .ToListAsync();
    }

    /// <summary>
    /// Get all roads created by a specific user
    /// </summary>
    public async Task<List<Road>> GetByCreatorAsync(string username)
    {
        return await _context.Roads
            .AsNoTracking()
            .Where(r => r.CreatedBy == username)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new Road
            {
                Id = r.Id,
                MapId = r.MapId,
                Name = r.Name,
                Waypoints = r.Waypoints,
                CreatedBy = r.CreatedBy,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                Hidden = r.Hidden
            })
            .ToListAsync();
    }

    /// <summary>
    /// Create a new road
    /// </summary>
    public async Task<Road> CreateAsync(Road road)
    {
        var entity = MapToEntity(road);
        _context.Roads.Add(entity);
        await _context.SaveChangesAsync();

        // Update domain object with generated ID
        road.Id = entity.Id;
        return road;
    }

    /// <summary>
    /// Update an existing road
    /// </summary>
    public async Task<Road> UpdateAsync(Road road)
    {
        // IMPORTANT: FindAsync bypasses tenant filters - use explicit tenant check
        var currentTenantId = _tenantContext.GetRequiredTenantId();
        var trackedEntity = await _context.Roads
            .FirstOrDefaultAsync(r => r.Id == road.Id && r.TenantId == currentTenantId);

        if (trackedEntity == null)
        {
            throw new KeyNotFoundException($"Road with ID {road.Id} not found.");
        }

        // Update properties
        trackedEntity.Name = road.Name;
        trackedEntity.Waypoints = road.Waypoints;
        trackedEntity.Hidden = road.Hidden;
        trackedEntity.UpdatedAt = road.UpdatedAt;
        // Note: CreatedAt, CreatedBy, MapId are immutable

        await _context.SaveChangesAsync();
        return MapToDomain(trackedEntity);
    }

    /// <summary>
    /// Delete a road
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        // IMPORTANT: FindAsync bypasses tenant filters - use explicit tenant check
        var currentTenantId = _tenantContext.GetCurrentTenantId();
        if (string.IsNullOrEmpty(currentTenantId))
            return;

        var entity = await _context.Roads
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == currentTenantId);
        if (entity != null)
        {
            _context.Roads.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Check if a road exists
    /// </summary>
    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Roads
            .AsNoTracking()
            .AnyAsync(r => r.Id == id);
    }

    /// <summary>
    /// Map entity to domain model
    /// </summary>
    private static Road MapToDomain(RoadEntity entity) => new()
    {
        Id = entity.Id,
        MapId = entity.MapId,
        Name = entity.Name,
        Waypoints = entity.Waypoints,
        CreatedBy = entity.CreatedBy,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        Hidden = entity.Hidden
    };

    /// <summary>
    /// Map domain model to entity
    /// </summary>
    private RoadEntity MapToEntity(Road road) => new()
    {
        Id = road.Id,
        MapId = road.MapId,
        Name = road.Name,
        Waypoints = road.Waypoints,
        CreatedBy = road.CreatedBy,
        CreatedAt = road.CreatedAt,
        UpdatedAt = road.UpdatedAt,
        Hidden = road.Hidden,
        TenantId = _tenantContext.GetRequiredTenantId()
    };
}
