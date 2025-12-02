namespace HnHMapperServer.Core.Models;

/// <summary>
/// Represents overlay data (claims, villages, provinces) for a grid coordinate
/// </summary>
public class OverlayData
{
    public int Id { get; set; }
    public int MapId { get; set; }
    public Coord Coord { get; set; } = new(0, 0);
    public string OverlayType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string TenantId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
