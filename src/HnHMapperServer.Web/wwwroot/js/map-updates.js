// Browser-based SSE (Server-Sent Events) client for map updates
// Uses native EventSource with automatic reconnection and cookie-based auth

let eventSource = null;
let dotnetRef = null;
let isConnecting = false;
let reconnectAttempts = 0;
const MAX_RECONNECT_DELAY = 5000; // 5 seconds max backoff

/**
 * Initialize SSE connection to /map/updates with browser cookies
 * @param {object} dotnetReference - DotNet object reference for callbacks
 * @returns {Promise<boolean>} - true if connection initiated successfully
 */
export async function initializeSseUpdates(dotnetReference) {
    console.warn('[SSE] ===== initializeSseUpdates called =====');
    console.warn('[SSE] dotnetReference:', dotnetReference);
    dotnetRef = dotnetReference;

    // Reuse existing connection if it's still working (prevents closing on Blazor reconnect)
    if (eventSource) {
        if (eventSource.readyState === EventSource.OPEN) {
            console.warn('[SSE] EventSource already connected and open, reusing existing connection');
            return true;  // Reuse existing connection
        } else {
            console.warn('[SSE] Closing broken EventSource (readyState: ' + eventSource.readyState + ' - 0=CONNECTING, 1=OPEN, 2=CLOSED)');
            eventSource.close();
            eventSource = null;
        }
    }

    console.warn('[SSE] Calling connectSse...');
    const result = connectSse();
    console.warn('[SSE] connectSse returned:', result);
    return result;
}

/**
 * Connect to SSE endpoint - browser automatically sends cookies
 */
function connectSse() {
    console.warn('[SSE] connectSse called - isConnecting:', isConnecting, 'eventSource:', eventSource);

    if (isConnecting || eventSource) {
        console.warn('[SSE] Already connecting or connected, returning false');
        return false;
    }

    isConnecting = true;

    try {
        console.warn('[SSE] Creating new EventSource for /map/updates');

        // EventSource automatically includes cookies for same-origin requests
        eventSource = new EventSource('/map/updates', {
            withCredentials: true
        });

        // Expose globally for notification-center.js to access
        window.mapUpdates = window.mapUpdates || {};
        window.mapUpdates.eventSource = eventSource;
        console.warn('[SSE] EventSource exposed as window.mapUpdates.eventSource');

        console.warn('[SSE] EventSource created successfully');
        console.warn('[SSE] EventSource.url:', eventSource.url);
        console.warn('[SSE] EventSource.readyState:', eventSource.readyState, '(0=CONNECTING, 1=OPEN, 2=CLOSED)');
        console.warn('[SSE] EventSource.withCredentials:', eventSource.withCredentials);

        // Wait a bit to see if readyState changes
        setTimeout(() => {
            console.warn('[SSE] EventSource readyState after 100ms:', eventSource?.readyState);
        }, 100);

        setTimeout(() => {
            console.warn('[SSE] EventSource readyState after 500ms:', eventSource?.readyState);
        }, 500);

        // Connection opened successfully
        eventSource.onopen = function () {
            console.warn('[SSE] ===== CONNECTED to /map/updates =====');
            isConnecting = false;
            reconnectAttempts = 0;
        };

        // Default message handler (tile updates - batched every 5s)
        eventSource.onmessage = function (event) {
            try {
                const tiles = JSON.parse(event.data);
                if (tiles && Array.isArray(tiles) && tiles.length > 0) {
                    invokeDotNetSafe('OnSseTileUpdates', tiles);
                }
            } catch (e) {
                console.error('[SSE] Error parsing tile updates:', e);
            }
        };

        // Map merge event
        eventSource.addEventListener('merge', function (event) {
            try {
                const merge = JSON.parse(event.data);
                invokeDotNetSafe('OnSseMapMerge', merge);
            } catch (e) {
                console.error('[SSE] Error parsing merge event:', e);
            }
        });

        // Map metadata update event (name, hidden, priority)
        eventSource.addEventListener('mapUpdate', function (event) {
            try {
                const mapInfo = JSON.parse(event.data);
                invokeDotNetSafe('OnSseMapUpdated', mapInfo);
            } catch (e) {
                console.error('[SSE] Error parsing mapUpdate event:', e);
            }
        });

        // Map deletion event
        eventSource.addEventListener('mapDelete', function (event) {
            try {
                const deleteInfo = JSON.parse(event.data);
                invokeDotNetSafe('OnSseMapDeleted', deleteInfo.Id);
            } catch (e) {
                console.error('[SSE] Error parsing mapDelete event:', e);
            }
        });

        // Map revision event (cache busting)
        eventSource.addEventListener('mapRevision', function (event) {
            try {
                const revision = JSON.parse(event.data);
                invokeDotNetSafe('OnSseMapRevision', revision.MapId, revision.Revision);
            } catch (e) {
                console.error('[SSE] Error parsing mapRevision event:', e);
            }
        });

        // Custom marker created event
        eventSource.addEventListener('customMarkerCreated', function (event) {
            try {
                const marker = JSON.parse(event.data);
                invokeDotNetSafe('OnCustomMarkerCreated', marker);
            } catch (e) {
                console.error('[SSE] Error parsing customMarkerCreated event:', e);
            }
        });

        // Custom marker updated event
        eventSource.addEventListener('customMarkerUpdated', function (event) {
            try {
                const marker = JSON.parse(event.data);
                invokeDotNetSafe('OnCustomMarkerUpdated', marker);
            } catch (e) {
                console.error('[SSE] Error parsing customMarkerUpdated event:', e);
            }
        });

        // Custom marker deleted event
        eventSource.addEventListener('customMarkerDeleted', function (event) {
            try {
                const deleteInfo = JSON.parse(event.data);
                invokeDotNetSafe('OnCustomMarkerDeleted', deleteInfo);
            } catch (e) {
                console.error('[SSE] Error parsing customMarkerDeleted event:', e);
            }
        });

        // Ping created event
        eventSource.addEventListener('pingCreated', function (event) {
            try {
                const ping = JSON.parse(event.data);
                invokeDotNetSafe('OnPingCreated', ping);
            } catch (e) {
                console.error('[SSE] Error parsing pingCreated event:', e);
            }
        });

        // Ping deleted event
        eventSource.addEventListener('pingDeleted', function (event) {
            try {
                const deleteInfo = JSON.parse(event.data);
                invokeDotNetSafe('OnPingDeleted', deleteInfo);
            } catch (e) {
                console.error('[SSE] Error parsing pingDeleted event:', e);
            }
        });

        // Timer created event
        eventSource.addEventListener('timerCreated', function (event) {
            try {
                const timer = JSON.parse(event.data);
                invokeDotNetSafe('OnTimerCreated', timer);
            } catch (e) {
                console.error('[SSE] Error parsing timerCreated event:', e);
            }
        });

        // Timer updated event
        eventSource.addEventListener('timerUpdated', function (event) {
            try {
                const timer = JSON.parse(event.data);
                invokeDotNetSafe('OnTimerUpdated', timer);
            } catch (e) {
                console.error('[SSE] Error parsing timerUpdated event:', e);
            }
        });

        // Timer completed event
        eventSource.addEventListener('timerCompleted', function (event) {
            try {
                const timer = JSON.parse(event.data);
                invokeDotNetSafe('OnTimerCompleted', timer);
            } catch (e) {
                console.error('[SSE] Error parsing timerCompleted event:', e);
            }
        });

        // Timer deleted event
        eventSource.addEventListener('timerDeleted', function (event) {
            try {
                const deleteInfo = JSON.parse(event.data);
                invokeDotNetSafe('OnTimerDeleted', deleteInfo.Id);
            } catch (e) {
                console.error('[SSE] Error parsing timerDeleted event:', e);
            }
        });

        // Road created event
        eventSource.addEventListener('roadCreated', function (event) {
            try {
                const road = JSON.parse(event.data);
                invokeDotNetSafe('OnRoadCreated', road);
            } catch (e) {
                console.error('[SSE] Error parsing roadCreated event:', e);
            }
        });

        // Road updated event
        eventSource.addEventListener('roadUpdated', function (event) {
            try {
                const road = JSON.parse(event.data);
                invokeDotNetSafe('OnRoadUpdated', road);
            } catch (e) {
                console.error('[SSE] Error parsing roadUpdated event:', e);
            }
        });

        // Road deleted event
        eventSource.addEventListener('roadDeleted', function (event) {
            try {
                const deleteInfo = JSON.parse(event.data);
                invokeDotNetSafe('OnRoadDeleted', deleteInfo);
            } catch (e) {
                console.error('[SSE] Error parsing roadDeleted event:', e);
            }
        });

        // Overlay updated event
        eventSource.addEventListener('overlayUpdated', function (event) {
            try {
                const overlay = JSON.parse(event.data);
                invokeDotNetSafe('OnOverlayUpdated', overlay);
            } catch (e) {
                console.error('[SSE] Error parsing overlayUpdated event:', e);
            }
        });

        // Characters snapshot event (initial full state)
        eventSource.addEventListener('charactersSnapshot', function (event) {
            console.warn('[SSE] ===== charactersSnapshot event received =====');
            console.warn('[SSE] Raw event data:', event.data);
            try {
                const characters = JSON.parse(event.data);
                console.warn('[SSE] Parsed characters array, length:', characters.length);
                if (characters.length > 0) {
                    console.warn('[SSE] First character:', characters[0]);
                }
                console.warn('[SSE] Calling OnSseCharactersSnapshot...');
                invokeDotNetSafe('OnSseCharactersSnapshot', characters);
                console.warn('[SSE] OnSseCharactersSnapshot call completed');
            } catch (e) {
                console.error('[SSE] Error parsing charactersSnapshot event:', e);
            }
        });

        // Character delta event (incremental updates)
        eventSource.addEventListener('characterDelta', function (event) {
            console.warn('[SSE] ===== characterDelta event received =====');
            console.warn('[SSE] Raw delta data:', event.data);
            try {
                const delta = JSON.parse(event.data);
                console.warn('[SSE] Parsed delta:', delta);
                invokeDotNetSafe('OnSseCharacterDelta', delta);
            } catch (e) {
                console.error('[SSE] Error parsing characterDelta event:', e);
            }
        });

        // Error handler - EventSource auto-reconnects, but we track attempts
        eventSource.onerror = function (error) {
            console.error('[SSE] ===== CONNECTION ERROR =====');
            console.error('[SSE] Error event:', error);
            console.error('[SSE] EventSource readyState:', eventSource?.readyState);
            console.error('[SSE] ReadyState meanings: 0=CONNECTING, 1=OPEN, 2=CLOSED');
            isConnecting = false;

            // EventSource automatically reconnects; we just track for logging
            reconnectAttempts++;
            console.error('[SSE] Reconnect attempt:', reconnectAttempts);

            // If auth fails (401), EventSource will keep retrying
            // The server-side fix ensures cookies work, so this should resolve
        };

        return true;
    } catch (e) {
        console.error('[SSE] Failed to create EventSource:', e);
        isConnecting = false;
        scheduleManualReconnect();
        return false;
    }
}

/**
 * Manual reconnect with exponential backoff (fallback if EventSource fails to auto-reconnect)
 */
function scheduleManualReconnect() {
    const baseDelay = reconnectAttempts === 0 ? 1000 : Math.min(reconnectAttempts * 1000, MAX_RECONNECT_DELAY);
    const jitter = Math.random() * 200 - 100; // ±100ms jitter
    const delay = Math.max(500, baseDelay + jitter);

    console.debug(`[SSE] Scheduling manual reconnect in ${delay}ms`);

    setTimeout(() => {
        if (!eventSource || eventSource.readyState === EventSource.CLOSED) {
            connectSse();
        }
    }, delay);
}

/**
 * Disconnect SSE stream
 */
export function disconnectSseUpdates() {
    if (eventSource) {
        console.debug('[SSE] Disconnecting');
        eventSource.close();
        eventSource = null;
    }
    isConnecting = false;
    reconnectAttempts = 0;
}

/**
 * Safely invoke .NET methods from JS; ignore calls during Blazor disconnects
 * Handles "No interop methods registered" error that occurs during circuit initialization
 */
function invokeDotNetSafe(method, ...args) {
    try {
        if (!dotnetRef) {
            console.debug('[SSE] dotnetRef is null, dropping call:', method);
            return null;
        }

        if (typeof dotnetRef.invokeMethodAsync !== 'function') {
            console.debug('[SSE] invokeMethodAsync not available yet, dropping call:', method);
            return null;
        }

        // Try to invoke the method, catching both sync and async errors
        try {
            const promise = dotnetRef.invokeMethodAsync(method, ...args);

            if (!promise || typeof promise.catch !== 'function') {
                console.debug('[SSE] invokeMethodAsync did not return a Promise, dropping:', method);
                return null;
            }

            // Handle Promise rejections (async errors)
            return promise.catch(err => {
                // Specifically catch "No interop methods are registered for renderer X" error
                // This happens when SSE events fire before Blazor circuit finishes initialization
                if (err && err.message) {
                    if (err.message.includes('No interop methods')) {
                        console.debug('[SSE] Circuit not ready yet, retrying in 100ms:', method);
                        // Retry once after 100ms (circuit should be ready by then)
                        setTimeout(() => {
                            console.debug('[SSE] Retrying after circuit init delay:', method);
                            invokeDotNetSafe(method, ...args);
                        }, 100);
                        return null;
                    }
                    if (err.message.includes('Cannot send data') || err.message.includes('not in the') || err.message.includes('Connected')) {
                        console.debug('[SSE] SignalR not connected yet, retrying in 100ms:', method);
                        setTimeout(() => invokeDotNetSafe(method, ...args), 100);
                        return null;
                    }
                }
                // For other errors, log and swallow (don't crash circuit)
                console.debug('[SSE] Error during JS->.NET call (swallowed):', method, err.message || err);
                return null;
            });
        } catch (invokeError) {
            // Catch synchronous errors from invokeMethodAsync itself
            // This handles "Cannot send data" errors that throw before returning a Promise
            if (invokeError && invokeError.message &&
                (invokeError.message.includes('Cannot send data') ||
                    invokeError.message.includes('not in the') ||
                    invokeError.message.includes('Connected') ||
                    invokeError.message.includes('No interop methods'))) {
                console.debug('[SSE] Circuit/SignalR not ready (sync error), retrying in 100ms:', method);
                setTimeout(() => invokeDotNetSafe(method, ...args), 100);
                return null;
            }
            throw invokeError; // Re-throw unexpected errors
        }
    } catch (e) {
        // Blazor circuit may be down; drop the call quietly
        console.debug('[SSE] JS->.NET call dropped:', method, e.message || e);
        return null;
    }
}

/**
 * Check if SSE is currently connected
 * @returns {boolean}
 */
export function isSseConnected() {
    return eventSource !== null && eventSource.readyState === EventSource.OPEN;
}



