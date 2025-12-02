namespace HnHMapperServer.Core.Models;

/// <summary>
/// Domain model for a road/path created by users on the map
/// </summary>
public class Road
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
    /// Waypoints as JSON array of coordinate objects
    /// Format: [{"coordX": 5, "coordY": 10, "x": 50, "y": 25}, ...]
    /// </summary>
    public string Waypoints { get; set; } = string.Empty;

    /// <summary>
    /// Username of creator
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp when last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Whether road is hidden
    /// </summary>
    public bool Hidden { get; set; }
}
