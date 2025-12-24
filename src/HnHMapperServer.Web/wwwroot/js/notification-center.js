// NotificationCenter JavaScript Interop
// Handles SSE events for notifications and calls Blazor component methods

window.notificationCenter = {
    dotNetRef: null,
    eventSource: null,
    isInitialized: false,
    listenersAttached: false, // Prevent duplicate listeners on reconnect

    /**
     * Initialize the notification center
     * @param {DotNetObjectReference} dotNetReference - Reference to Blazor component
     */
    init: function (dotNetReference) {
        this.dotNetRef = dotNetReference;
        this.isInitialized = true;
        console.log('[NotificationCenter] Initialized');

        // Listen to SSE events from the global event source
        // The map-updates.js should already have established the SSE connection
        this.attachEventListeners();
    },

    /**
     * Attach event listeners to the global SSE event source
     */
    attachEventListeners: function () {
        // Prevent duplicate listeners on circuit reconnect
        if (this.listenersAttached) {
            console.log('[NotificationCenter] Listeners already attached, skipping');
            return;
        }

        // Wait for the global SSE connection to be established
        // Poll every 500ms (not 100ms) to reduce CPU during initialization
        const checkConnection = setInterval(() => {
            if (window.mapUpdates && window.mapUpdates.eventSource) {
                const eventSource = window.mapUpdates.eventSource;
                this.listenersAttached = true;

                // Listen for notification created events
                eventSource.addEventListener('notificationCreated', (e) => {
                    try {
                        const notification = JSON.parse(e.data);
                        console.log('[NotificationCenter] Notification created:', notification);

                        // Call Blazor component
                        if (this.dotNetRef && this.isInitialized) {
                            this.dotNetRef.invokeMethodAsync('OnNotificationReceived', notification);

                            // Show toast/snackbar notification
                            this.dotNetRef.invokeMethodAsync('ShowSnackbarNotification', notification);

                            // Show browser notification if enabled
                            this.showBrowserNotification(notification);

                            // Play sound if enabled
                            this.playNotificationSound(notification.Type);
                        }
                    } catch (error) {
                        console.error('[NotificationCenter] Error parsing notification:', error);
                    }
                });

                // Listen for notification read events
                eventSource.addEventListener('notificationRead', (e) => {
                    try {
                        const data = JSON.parse(e.data);
                        console.log('[NotificationCenter] Notification read:', data.Id);

                        if (this.dotNetRef && this.isInitialized) {
                            this.dotNetRef.invokeMethodAsync('OnNotificationRead', data.Id);
                        }
                    } catch (error) {
                        console.error('[NotificationCenter] Error parsing notification read event:', error);
                    }
                });

                // Listen for notification dismissed events
                eventSource.addEventListener('notificationDismissed', (e) => {
                    try {
                        const data = JSON.parse(e.data);
                        console.log('[NotificationCenter] Notification dismissed:', data.Id);

                        if (this.dotNetRef && this.isInitialized) {
                            this.dotNetRef.invokeMethodAsync('OnNotificationDismissed', data.Id);
                        }
                    } catch (error) {
                        console.error('[NotificationCenter] Error parsing notification dismissed event:', error);
                    }
                });

                clearInterval(checkConnection);
                console.log('[NotificationCenter] Event listeners attached');
            }
        }, 500); // Poll every 500ms (not 100ms) to reduce CPU

        // Stop checking after 10 seconds
        setTimeout(() => {
            clearInterval(checkConnection);
        }, 10000);
    },

    /**
     * Show browser notification (if permission granted)
     * @param {object} notification - Notification data
     */
    showBrowserNotification: function (notification) {
        if (!('Notification' in window)) {
            return;
        }

        if (Notification.permission === 'granted') {
            const options = {
                body: notification.Message,
                icon: '/favicon.ico',
                badge: '/favicon.ico',
                tag: `notification-${notification.Id}`,
                requireInteraction: notification.Priority === 'High',
                silent: false
            };

            const browserNotification = new Notification(notification.Title, options);

            // Auto-close after 5 seconds (unless high priority)
            if (notification.Priority !== 'High') {
                setTimeout(() => {
                    browserNotification.close();
                }, 5000);
            }

            // Handle click
            browserNotification.onclick = () => {
                window.focus();
                browserNotification.close();

                // Trigger navigation via Blazor
                if (this.dotNetRef && this.isInitialized) {
                    // The notification will be handled by the component's click handler
                }
            };
        } else if (Notification.permission === 'default') {
            // Request permission
            Notification.requestPermission();
        }
    },

    /**
     * Play notification sound
     * @param {string} notificationType - Type of notification
     */
    playNotificationSound: function (notificationType) {
        try {
            let soundFile = '/sounds/notification.mp3';

            // Use different sound for timer warnings and expiration
            if (notificationType === 'TimerPreExpiryWarning') {
                soundFile = '/sounds/timer-warning.mp3';
            } else if (notificationType === 'MarkerTimerExpired' || notificationType === 'StandaloneTimerExpired') {
                soundFile = '/sounds/timer-expired.mp3';
            }

            const audio = new Audio(soundFile);
            audio.volume = 0.5; // 50% volume
            // Cleanup after playback ends to prevent memory leak
            audio.onended = () => { audio.src = ''; };
            audio.play().catch((error) => {
                console.warn('[NotificationCenter] Could not play sound:', error);
                audio.src = ''; // Cleanup on error too
            });
        } catch (error) {
            console.warn('[NotificationCenter] Error playing sound:', error);
        }
    },

    /**
     * Request browser notification permission
     */
    requestNotificationPermission: async function () {
        if (!('Notification' in window)) {
            console.warn('[NotificationCenter] Browser notifications not supported');
            return false;
        }

        if (Notification.permission === 'granted') {
            return true;
        }

        if (Notification.permission !== 'denied') {
            const permission = await Notification.requestPermission();
            return permission === 'granted';
        }

        return false;
    },

    /**
     * Dispose resources
     */
    dispose: function () {
        console.log('[NotificationCenter] Disposed');
        this.dotNetRef = null;
        this.isInitialized = false;
    }
};
