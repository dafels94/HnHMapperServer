namespace HnHMapperServer.Core.DTOs;

/// <summary>
/// Represents a single waypoint in a road
/// </summary>
public class RoadWaypointDto
{
    /// <summary>
    /// Grid coordinate X
    /// </summary>
    public int CoordX { get; set; }

    /// <summary>
    /// Grid coordinate Y
    /// </summary>
    public int CoordY { get; set; }

    /// <summary>
    /// Position X within the grid (0-100)
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Position Y within the grid (0-100)
    /// </summary>
    public int Y { get; set; }
}

/// <summary>
/// DTO for creating a new road
/// </summary>
public class CreateRoadDto
{
    /// <summary>
    /// Map ID where the road is placed
    /// </summary>
    public int MapId { get; set; }

    /// <summary>
    /// Road name (max 80 characters, required)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of waypoints defining the road path (minimum 2 required)
    /// </summary>
    public List<RoadWaypointDto> Waypoints { get; set; } = new();
}

/// <summary>
/// DTO for updating an existing road
/// </summary>
public class UpdateRoadDto
{
    /// <summary>
    /// Road name (max 80 characters, required)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of waypoints defining the road path (minimum 2 required)
    /// </summary>
    public List<RoadWaypointDto> Waypoints { get; set; } = new();

    /// <summary>
    /// Whether the road is hidden
    /// </summary>
    public bool Hidden { get; set; }
}

/// <summary>
/// DTO for viewing/listing roads
/// </summary>
public class RoadViewDto
{
    /// <summary>
    /// Road ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Map ID where the road is placed
    /// </summary>
    public int MapId { get; set; }

    /// <summary>
    /// Road name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of waypoints defining the road path
    /// </summary>
    public List<RoadWaypointDto> Waypoints { get; set; } = new();

    /// <summary>
    /// Username of the creator
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the road was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the road was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Whether the road is hidden
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Whether the current user can edit this road
    /// </summary>
    public bool CanEdit { get; set; }
}

/// <summary>
/// DTO for road SSE events (real-time updates)
/// </summary>
public class RoadEventDto
{
    /// <summary>
    /// Road ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Map ID
    /// </summary>
    public int MapId { get; set; }

    /// <summary>
    /// Road name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of waypoints defining the road path
    /// </summary>
    public List<RoadWaypointDto> Waypoints { get; set; } = new();

    /// <summary>
    /// Username of creator
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Whether road is hidden
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// DTO for road delete events (SSE)
/// </summary>
public class RoadDeleteEventDto
{
    /// <summary>
    /// Road ID that was deleted
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenancy isolation
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
}
