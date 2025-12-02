using HnHMapperServer.Core.Models;

namespace HnHMapperServer.Core.Interfaces;

/// <summary>
/// Repository interface for road operations
/// </summary>
public interface IRoadRepository
{
    /// <summary>
    /// Get a road by ID
    /// </summary>
    Task<Road?> GetByIdAsync(int id);

    /// <summary>
    /// Get all roads for a specific map
    /// </summary>
    Task<List<Road>> GetByMapIdAsync(int mapId);

    /// <summary>
    /// Get all roads created by a specific user
    /// </summary>
    Task<List<Road>> GetByCreatorAsync(string username);

    /// <summary>
    /// Create a new road
    /// </summary>
    Task<Road> CreateAsync(Road road);

    /// <summary>
    /// Update an existing road
    /// </summary>
    Task<Road> UpdateAsync(Road road);

    /// <summary>
    /// Delete a road
    /// </summary>
    Task DeleteAsync(int id);

    /// <summary>
    /// Check if a road exists
    /// </summary>
    Task<bool> ExistsAsync(int id);
}
