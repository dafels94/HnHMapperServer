namespace HnHMapperServer.Web.Components.Map.Sidebar;

/// <summary>
/// Enumeration of sidebar panel modes for the map viewer.
/// Determines which panel is currently active in the right sidebar.
/// </summary>
public enum SidebarMode
{
    /// <summary>
    /// Players panel - shows active players/characters with filtering and follow functionality
    /// </summary>
    Players,
    
    /// <summary>
    /// Markers panel - shows all markers with filtering and navigation
    /// </summary>
    Markers,
    
    /// <summary>
    /// Layers panel - controls visibility of map layers (players, markers, thingwalls, quests, etc.)
    /// </summary>
    Layers,
    
    /// <summary>
    /// Maps panel - controls for map selection, overlay, zoom, and grid coordinates
    /// </summary>
    Maps,

    /// <summary>
    /// Events panel - shows active timers, timer history, and allows timer creation
    /// </summary>
    Events,

    /// <summary>
    /// Roads panel - shows user-drawn roads with filtering and navigation
    /// </summary>
    Roads
}








