// Road Manager Module
// Handles user-drawn road/path management

import { TileSize, HnHMaxZoom, HnHMinZoom } from './leaflet-config.js';

// Road storage
const roads = {};
let currentMapId = 0;
let roadLayer = null;
let roadsInitialized = false;
let pendingRoads = [];

// Drawing state
let isDrawingRoad = false;
let currentDrawingPoints = [];
let tempPolyline = null;
let tempMarkers = [];

// Safely invoke .NET methods from JS
let invokeDotNetSafe = null;
let mapInstanceRef = null;

// Visual styles
const ROAD_STYLE = {
    color: '#FFFFFF', // White
    weight: 4,
    opacity: 0.85
};

const ROAD_HOVER_STYLE = {
    color: '#E0E0E0', // Light gray for hover
    weight: 6,
    opacity: 1
};

const WAYPOINT_COLORS = {
    start: '#22C55E',      // Green
    intermediate: '#3B82F6', // Blue
    end: '#EF4444'          // Red
};

/**
 * Initialize road manager
 * @param {object} layer - Road layer group
 * @param {function} invokeFunc - Function to invoke .NET methods
 */
export function initializeRoadManager(layer, invokeFunc) {
    console.log('[Road] Initializing with invokeFunc:', typeof invokeFunc);
    roadLayer = layer;
    invokeDotNetSafe = invokeFunc;
    roadsInitialized = true;
    flushRoadQueue();
}

/**
 * Set the current map ID for road filtering
 */
export function setCurrentMapId(mapId) {
    currentMapId = mapId;
}

/**
 * Set map instance reference
 */
export function setMapInstance(instance) {
    mapInstanceRef = instance;
}

/**
 * Convert waypoint to Leaflet LatLng
 */
function waypointToLatLng(wp, mapInstance) {
    const absX = wp.coordX * TileSize + wp.x;
    const absY = wp.coordY * TileSize + wp.y;
    return mapInstance.unproject([absX, absY], HnHMaxZoom);
}

/**
 * Convert waypoints array to LatLng array
 */
function waypointsToLatLngs(waypoints, mapInstance) {
    return waypoints.map(wp => waypointToLatLng(wp, mapInstance));
}

/**
 * Create waypoint markers for display (start=green, end=red, intermediate=blue)
 * @param {array} waypoints - Array of waypoint DTOs
 * @param {object} mapInstance - Leaflet map instance
 * @returns {array} - Array of circle markers
 */
function createWaypointMarkers(waypoints, mapInstance) {
    if (!waypoints || waypoints.length < 2) return [];

    const markers = [];
    waypoints.forEach((wp, index) => {
        const latlng = waypointToLatLng(wp, mapInstance);
        let color, radius;

        if (index === 0) {
            color = WAYPOINT_COLORS.start;
            radius = 8;
        } else if (index === waypoints.length - 1) {
            color = WAYPOINT_COLORS.end;
            radius = 8;
        } else {
            color = WAYPOINT_COLORS.intermediate;
            radius = 5;
        }

        const marker = L.circleMarker(latlng, {
            radius: radius,
            color: color,
            fillColor: color,
            fillOpacity: 0.8,
            weight: 2
        });
        markers.push(marker);
    });

    return markers;
}

/**
 * Add a road to the map
 * @param {object} road - Road object with id, mapId, name, waypoints, createdBy, etc.
 * @param {object} mapInstance - Leaflet map instance
 */
export function addRoad(road, mapInstance) {
    if (!roadLayer || !mapInstance) return;

    const normalized = normalizeRoad(road);

    if (!normalized) {
        console.warn('[Road] Unable to normalize road payload', road);
        return;
    }

    if (!roadsInitialized) {
        pendingRoads.push(road);
        return;
    }

    if (normalized.mapId && normalized.mapId !== currentMapId) {
        pendingRoads.push(road);
        return;
    }

    if (!normalized.waypoints || normalized.waypoints.length < 2) {
        console.warn('[Road] Road has insufficient waypoints', normalized.id);
        return;
    }

    try {
        const latlngs = waypointsToLatLngs(normalized.waypoints, mapInstance);

        // Create polyline for the road
        const polyline = L.polyline(latlngs, {
            ...ROAD_STYLE,
            interactive: true
        });

        // Create road label (shown along the line)
        polyline.bindTooltip(normalized.name, {
            permanent: false,
            sticky: true,
            direction: 'top',
            offset: [0, -10],
            className: 'road-tooltip'
        });

        // Create waypoint markers (hidden by default, shown on hover)
        const waypointMarkers = createWaypointMarkers(normalized.waypoints, mapInstance);
        const waypointLayer = L.layerGroup(waypointMarkers);

        // Hover effect - highlight road and show waypoints
        polyline.on('mouseover', () => {
            polyline.setStyle(ROAD_HOVER_STYLE);
            if (!mapInstance.hasLayer(waypointLayer)) {
                waypointLayer.addTo(mapInstance);
            }
        });

        polyline.on('mouseout', () => {
            polyline.setStyle(ROAD_STYLE);
            if (mapInstance.hasLayer(waypointLayer)) {
                mapInstance.removeLayer(waypointLayer);
            }
        });

        // Context menu for editing/deleting
        polyline.on('contextmenu', (e) => {
            L.DomEvent.stopPropagation(e);
            const screenX = Math.floor(e.containerPoint.x);
            const screenY = Math.floor(e.containerPoint.y);

            if (typeof invokeDotNetSafe === 'function') {
                invokeDotNetSafe('JsOnRoadContextMenu', normalized.id, screenX, screenY);
            }
        });

        // Store road data
        roads[normalized.id] = {
            polyline,
            waypointLayer,
            data: normalized
        };

        roadLayer.addLayer(polyline);
    } catch (err) {
        console.error('[Road] Error adding road:', err, normalized);
    }
}

/**
 * Flush queued roads (add roads that were waiting for map switch)
 * @param {object} mapInstance - Leaflet map instance
 */
export function flushRoadQueue(mapInstance) {
    if (!roadsInitialized || !mapInstance || pendingRoads.length === 0) {
        return;
    }

    const remaining = [];
    for (const road of pendingRoads) {
        const normalized = normalizeRoad(road);
        if (normalized && normalized.mapId && normalized.mapId !== currentMapId) {
            remaining.push(road);
            continue;
        }
        addRoad(road, mapInstance);
    }

    pendingRoads = remaining;
}

/**
 * Update an existing road
 * @param {number} roadId - Road ID
 * @param {object} road - Updated road object
 * @param {object} mapInstance - Leaflet map instance
 */
export function updateRoad(roadId, road, mapInstance) {
    removeRoad(roadId);
    addRoad(road, mapInstance);
}

/**
 * Remove a road from the map
 * @param {number} roadId - Road ID to remove
 */
export function removeRoad(roadId) {
    if (!roadLayer) return;

    const roadData = roads[roadId];
    if (roadData) {
        roadLayer.removeLayer(roadData.polyline);
        if (roadData.waypointLayer && mapInstanceRef) {
            mapInstanceRef.removeLayer(roadData.waypointLayer);
        }
        delete roads[roadId];
    }
}

/**
 * Clear all roads from the map
 */
export function clearAllRoads() {
    if (!roadLayer) return;

    roadLayer.clearLayers();
    Object.keys(roads).forEach(id => {
        if (roads[id].waypointLayer && mapInstanceRef) {
            mapInstanceRef.removeLayer(roads[id].waypointLayer);
        }
        delete roads[id];
    });
}

/**
 * Toggle road layer visibility
 * @param {boolean} visible - Whether to show roads
 * @param {object} mapInstance - Leaflet map instance
 */
export function toggleRoads(visible, mapInstance) {
    if (!roadLayer || !mapInstance) return;

    if (visible) {
        if (!mapInstance.hasLayer(roadLayer)) {
            roadLayer.addTo(mapInstance);
        }
    } else {
        if (mapInstance.hasLayer(roadLayer)) {
            mapInstance.removeLayer(roadLayer);
        }
        // Also hide any visible waypoint layers
        Object.values(roads).forEach(roadData => {
            if (roadData.waypointLayer && mapInstance.hasLayer(roadData.waypointLayer)) {
                mapInstance.removeLayer(roadData.waypointLayer);
            }
        });
    }
}

/**
 * Jump map view to a road
 * @param {number} roadId - Road ID to jump to
 * @param {object} mapInstance - Leaflet map instance
 * @returns {boolean} - True if road was found
 */
export function jumpToRoad(roadId, mapInstance) {
    const roadData = roads[roadId];
    if (!roadData) {
        console.warn('[Road] jumpToRoad failed; road not found', roadId);
        return false;
    }

    const bounds = roadData.polyline.getBounds();
    mapInstance.fitBounds(bounds, { padding: [50, 50] });
    return true;
}

// ============ Drawing Mode Functions ============

/**
 * Start drawing a new road
 * @param {object} mapInstance - Leaflet map instance
 */
export function startDrawingRoad(mapInstance) {
    if (!mapInstance) return;

    isDrawingRoad = true;
    currentDrawingPoints = [];
    mapInstanceRef = mapInstance;

    // Change cursor
    mapInstance.getContainer().style.cursor = 'crosshair';

    // Add click handler for adding points
    mapInstance.on('click', onDrawingClick);
    mapInstance.on('dblclick', onDrawingDoubleClick);

    // Prevent map zoom on double-click while drawing
    mapInstance.doubleClickZoom.disable();

    console.log('[Road] Drawing mode started');
}

/**
 * Handle click during drawing mode
 */
function onDrawingClick(e) {
    if (!isDrawingRoad) return;

    const mapInstance = e.target;
    const latlng = e.latlng;
    currentDrawingPoints.push(latlng);

    // Determine marker color based on position
    let color;
    if (currentDrawingPoints.length === 1) {
        color = WAYPOINT_COLORS.start;
    } else {
        // All subsequent points are intermediate (will update last to red on finish)
        color = WAYPOINT_COLORS.intermediate;
    }

    // Add temporary marker at click point
    const marker = L.circleMarker(latlng, {
        radius: 6,
        color: color,
        fillColor: color,
        fillOpacity: 0.8,
        weight: 2
    });
    tempMarkers.push(marker);
    marker.addTo(mapInstance);

    // Update temporary polyline
    if (tempPolyline) {
        tempPolyline.setLatLngs(currentDrawingPoints);
    } else if (currentDrawingPoints.length >= 2) {
        tempPolyline = L.polyline(currentDrawingPoints, {
            color: '#FFFFFF',
            weight: 3,
            dashArray: '8, 8',
            opacity: 0.7
        }).addTo(mapInstance);
    }

    console.log('[Road] Point added, total:', currentDrawingPoints.length);
}

/**
 * Handle double-click to finish drawing
 */
function onDrawingDoubleClick(e) {
    if (!isDrawingRoad || currentDrawingPoints.length < 2) return;
    L.DomEvent.stopPropagation(e);
    L.DomEvent.preventDefault(e);
    finishDrawingRoad(e.target);
}

/**
 * Finish drawing and return waypoints
 * @param {object} mapInstance - Leaflet map instance
 */
export function finishDrawingRoad(mapInstance) {
    if (!mapInstance || currentDrawingPoints.length < 2) {
        cancelDrawingRoad(mapInstance);
        return;
    }

    isDrawingRoad = false;
    mapInstance.getContainer().style.cursor = '';
    mapInstance.off('click', onDrawingClick);
    mapInstance.off('dblclick', onDrawingDoubleClick);
    mapInstance.doubleClickZoom.enable();

    // Convert latlngs to waypoint format
    const waypoints = currentDrawingPoints.map(latlng => {
        const point = mapInstance.project(latlng, HnHMaxZoom);
        const coordX = Math.floor(point.x / TileSize);
        const coordY = Math.floor(point.y / TileSize);
        const x = Math.floor(((point.x % TileSize) + TileSize) % TileSize);
        const y = Math.floor(((point.y % TileSize) + TileSize) % TileSize);
        return { coordX, coordY, x, y };
    });

    // Remove temporary markers and polyline
    cleanupDrawingTemp(mapInstance);

    // Notify Blazor with waypoints
    console.log('[Road] Finishing drawing with', waypoints.length, 'waypoints, mapId:', currentMapId);
    console.log('[Road] invokeDotNetSafe type:', typeof invokeDotNetSafe);
    console.log('[Road] Waypoints:', JSON.stringify(waypoints));

    if (typeof invokeDotNetSafe === 'function') {
        try {
            invokeDotNetSafe('JsOnRoadDrawingComplete', currentMapId, waypoints);
            console.log('[Road] invokeDotNetSafe called successfully');
        } catch (err) {
            console.error('[Road] Error calling invokeDotNetSafe:', err);
        }
    } else {
        console.error('[Road] invokeDotNetSafe is NOT a function! Type:', typeof invokeDotNetSafe);
    }

    currentDrawingPoints = [];
}

/**
 * Cancel drawing mode
 * @param {object} mapInstance - Leaflet map instance
 */
export function cancelDrawingRoad(mapInstance) {
    if (!mapInstance) return;

    isDrawingRoad = false;
    mapInstance.getContainer().style.cursor = '';
    mapInstance.off('click', onDrawingClick);
    mapInstance.off('dblclick', onDrawingDoubleClick);
    mapInstance.doubleClickZoom.enable();

    cleanupDrawingTemp(mapInstance);
    currentDrawingPoints = [];

    console.log('[Road] Drawing cancelled');
}

/**
 * Check if currently in drawing mode
 */
export function isInDrawingMode() {
    return isDrawingRoad;
}

/**
 * Get current drawing points count
 */
export function getDrawingPointsCount() {
    return currentDrawingPoints.length;
}

/**
 * Clean up temporary drawing elements
 */
function cleanupDrawingTemp(mapInstance) {
    tempMarkers.forEach(m => {
        if (mapInstance.hasLayer(m)) {
            mapInstance.removeLayer(m);
        }
    });
    tempMarkers = [];

    if (tempPolyline) {
        if (mapInstance.hasLayer(tempPolyline)) {
            mapInstance.removeLayer(tempPolyline);
        }
        tempPolyline = null;
    }
}

// ============ Helper Functions ============

/**
 * Normalize road object from API response
 */
function normalizeRoad(road) {
    if (!road || typeof road !== 'object') {
        return null;
    }

    const coerceNumber = (value, fallback = 0) => {
        if (typeof value === 'number') {
            return Number.isFinite(value) ? value : fallback;
        }
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    };

    const coerceBool = (value, fallback = false) => {
        if (typeof value === 'boolean') return value;
        if (typeof value === 'string') {
            if (value.toLowerCase() === 'true') return true;
            if (value.toLowerCase() === 'false') return false;
        }
        return fallback;
    };

    const resolve = (lower, upper, fallback = undefined) => {
        if (lower !== undefined) return lower;
        if (upper !== undefined) return upper;
        return fallback;
    };

    const id = coerceNumber(resolve(road.id, road.Id), 0);
    const mapId = coerceNumber(resolve(road.mapId, road.MapId), 0);
    const name = resolve(road.name, road.Name) || 'Unnamed Road';
    const hidden = coerceBool(resolve(road.hidden, road.Hidden), false);
    const createdBy = resolve(road.createdBy, road.CreatedBy) || 'Unknown';

    // Parse waypoints - handle both camelCase and PascalCase
    let waypoints = resolve(road.waypoints, road.Waypoints) || [];
    if (typeof waypoints === 'string') {
        try {
            waypoints = JSON.parse(waypoints);
        } catch {
            waypoints = [];
        }
    }

    // Normalize each waypoint
    waypoints = waypoints.map(wp => ({
        coordX: coerceNumber(resolve(wp.coordX, wp.CoordX), 0),
        coordY: coerceNumber(resolve(wp.coordY, wp.CoordY), 0),
        x: coerceNumber(resolve(wp.x, wp.X), 0),
        y: coerceNumber(resolve(wp.y, wp.Y), 0)
    }));

    return {
        id,
        mapId,
        name,
        waypoints,
        createdBy,
        hidden
    };
}
