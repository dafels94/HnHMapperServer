using HnHMapperServer.Core.DTOs;

namespace HnHMapperServer.Web.Models;

/// <summary>
/// View model for roads in the UI
/// </summary>
public class RoadViewModel
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

    /// <summary>
    /// Get relative time string (e.g., "2 hours ago")
    /// </summary>
    public string RelativeTime => GetRelativeTime(CreatedAt);

    /// <summary>
    /// Get number of waypoints
    /// </summary>
    public int WaypointCount => Waypoints?.Count ?? 0;

    /// <summary>
    /// Calculate relative time from UTC timestamp
    /// </summary>
    private static string GetRelativeTime(DateTime dt)
    {
        if (dt <= DateTime.MinValue.AddSeconds(1))
        {
            return "–";
        }

        var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        var span = DateTime.UtcNow - utc;

        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 2) return "1 minute ago";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} minutes ago";
        if (span.TotalHours < 2) return "1 hour ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
        if (span.TotalDays < 2) return "yesterday";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} days ago";
        if (span.TotalDays < 14) return "last week";
        if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)} weeks ago";

        return utc.ToLocalTime().ToString("MMM d, yyyy");
    }

    /// <summary>
    /// Create from RoadViewDto
    /// </summary>
    public static RoadViewModel FromDto(RoadViewDto dto)
    {
        return new RoadViewModel
        {
            Id = dto.Id,
            MapId = dto.MapId,
            Name = dto.Name,
            Waypoints = dto.Waypoints,
            CreatedBy = dto.CreatedBy,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            Hidden = dto.Hidden,
            CanEdit = dto.CanEdit
        };
    }

    /// <summary>
    /// Create from RoadEventDto (for SSE events)
    /// </summary>
    public static RoadViewModel FromEventDto(RoadEventDto dto, bool canEdit)
    {
        return new RoadViewModel
        {
            Id = dto.Id,
            MapId = dto.MapId,
            Name = dto.Name,
            Waypoints = dto.Waypoints,
            CreatedBy = dto.CreatedBy,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.CreatedAt,
            Hidden = dto.Hidden,
            CanEdit = canEdit
        };
    }
}
