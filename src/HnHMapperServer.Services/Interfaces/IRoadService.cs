using HnHMapperServer.Core.DTOs;

namespace HnHMapperServer.Services.Interfaces;

/// <summary>
/// Service interface for road operations with validation and authorization
/// </summary>
public interface IRoadService
{
    /// <summary>
    /// Get all roads for a specific map
    /// </summary>
    /// <param name="mapId">Map ID</param>
    /// <param name="currentUsername">Current user's username</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    Task<List<RoadViewDto>> GetByMapIdAsync(int mapId, string currentUsername, bool isAdmin);

    /// <summary>
    /// Get a road by ID
    /// </summary>
    /// <param name="id">Road ID</param>
    /// <param name="currentUsername">Current user's username</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    Task<RoadViewDto?> GetByIdAsync(int id, string currentUsername, bool isAdmin);

    /// <summary>
    /// Create a new road
    /// </summary>
    /// <param name="dto">Create road DTO</param>
    /// <param name="currentUsername">Current user's username (will be set as creator)</param>
    /// <returns>Created road view DTO</returns>
    Task<RoadViewDto> CreateAsync(CreateRoadDto dto, string currentUsername);

    /// <summary>
    /// Update an existing road
    /// </summary>
    /// <param name="id">Road ID</param>
    /// <param name="dto">Update road DTO</param>
    /// <param name="currentUsername">Current user's username</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <returns>Updated road view DTO</returns>
    Task<RoadViewDto> UpdateAsync(int id, UpdateRoadDto dto, string currentUsername, bool isAdmin);

    /// <summary>
    /// Delete a road
    /// </summary>
    /// <param name="id">Road ID</param>
    /// <param name="currentUsername">Current user's username</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    Task DeleteAsync(int id, string currentUsername, bool isAdmin);
}
