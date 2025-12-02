using Microsoft.JSInterop;

namespace HnHMapperServer.Web.Services;

/// <summary>
/// Provides safe wrappers around JS Interop that swallow benign exceptions during SignalR reconnects.
/// Prevents circuit crashes when Blazor Server briefly loses connection ("rejoining server").
/// </summary>
public class SafeJsInterop
{
    private readonly ILogger<SafeJsInterop> _logger;
    private readonly IJSRuntime _jsRuntime;

    public SafeJsInterop(ILogger<SafeJsInterop> logger, IJSRuntime jsRuntime)
    {
        _logger = logger;
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Safely invoke a void JS method, swallowing benign disconnect exceptions.
    /// </summary>
    /// <param name="module">JS module reference</param>
    /// <param name="identifier">JS function name</param>
    /// <param name="args">Function arguments</param>
    /// <returns>True if successful, false if swallowed exception</returns>
    public async Task<bool> InvokeVoidSafeAsync(IJSObjectReference? module, string identifier, params object?[]? args)
    {
        if (module == null)
        {
            try { await _jsRuntime.InvokeVoidAsync("console.error", $"[SafeJsInterop] ERROR: module is null for {identifier}"); } catch { }
            _logger.LogDebug("InvokeVoidSafe: module is null for {Identifier}", identifier);
            return false;
        }

        try
        {
            await module.InvokeVoidAsync(identifier, args);
            return true;
        }
        catch (TaskCanceledException ex)
        {
            try { await _jsRuntime.InvokeVoidAsync("console.warn", $"[SafeJsInterop] {identifier} - TaskCanceledException"); } catch { }
            _logger.LogDebug("InvokeVoidSafe: TaskCanceledException for {Identifier} (reconnecting)", identifier);
            return false;
        }
        catch (JSDisconnectedException ex)
        {
            try { await _jsRuntime.InvokeVoidAsync("console.warn", $"[SafeJsInterop] {identifier} - JSDisconnectedException"); } catch { }
            _logger.LogDebug("InvokeVoidSafe: JSDisconnectedException for {Identifier} (circuit lost)", identifier);
            return false;
        }
        catch (ObjectDisposedException ex)
        {
            try { await _jsRuntime.InvokeVoidAsync("console.warn", $"[SafeJsInterop] {identifier} - ObjectDisposedException"); } catch { }
            _logger.LogDebug("InvokeVoidSafe: ObjectDisposedException for {Identifier} (disposed)", identifier);
            return false;
        }
        catch (Exception ex)
        {
            try { await _jsRuntime.InvokeVoidAsync("console.error", $"[SafeJsInterop] {identifier} - EXCEPTION: {ex.GetType().Name}: {ex.Message}\nStack: {ex.StackTrace}"); } catch { }
            _logger.LogWarning(ex, "InvokeVoidSafe: Unexpected exception for {Identifier}", identifier);
            return false;
        }
    }

    /// <summary>
    /// Safely invoke a JS method that returns a value, swallowing benign disconnect exceptions.
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="module">JS module reference</param>
    /// <param name="identifier">JS function name</param>
    /// <param name="args">Function arguments</param>
    /// <returns>Result value or default(T) if exception swallowed</returns>
    public async Task<T?> InvokeSafeAsync<T>(IJSObjectReference? module, string identifier, params object?[]? args)
    {
        if (module == null)
        {
            _logger.LogDebug("InvokeSafe: module is null for {Identifier}", identifier);
            return default;
        }

        try
        {
            return await module.InvokeAsync<T>(identifier, args);
        }
        catch (TaskCanceledException)
        {
            // Blazor was reconnecting; circuit was cancelled
            _logger.LogDebug("InvokeSafe: TaskCanceledException for {Identifier} (reconnecting)", identifier);
            return default;
        }
        catch (JSDisconnectedException)
        {
            // JS runtime disconnected (circuit lost)
            _logger.LogDebug("InvokeSafe: JSDisconnectedException for {Identifier} (circuit lost)", identifier);
            return default;
        }
        catch (ObjectDisposedException)
        {
            // Component was disposed while operation was in progress
            _logger.LogDebug("InvokeSafe: ObjectDisposedException for {Identifier} (disposed)", identifier);
            return default;
        }
        catch (Exception ex)
        {
            // Unexpected exception - log as warning
            _logger.LogWarning(ex, "InvokeSafe: Unexpected exception for {Identifier}", identifier);
            return default;
        }
    }

    /// <summary>
    /// Safely get a value from localStorage.
    /// </summary>
    /// <param name="key">localStorage key</param>
    /// <returns>Value or null if not found or exception swallowed</returns>
    public async Task<string?> GetLocalStorageAsync(string key)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("GetLocalStorage: TaskCanceledException for {Key} (reconnecting)", key);
            return null;
        }
        catch (JSDisconnectedException)
        {
            _logger.LogDebug("GetLocalStorage: JSDisconnectedException for {Key} (circuit lost)", key);
            return null;
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("GetLocalStorage: ObjectDisposedException for {Key} (disposed)", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetLocalStorage: Unexpected exception for {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Safely set a value in localStorage.
    /// </summary>
    /// <param name="key">localStorage key</param>
    /// <param name="value">Value to store</param>
    /// <returns>True if successful, false if exception swallowed</returns>
    public async Task<bool> SetLocalStorageAsync(string key, string value)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);
            return true;
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("SetLocalStorage: TaskCanceledException for {Key} (reconnecting)", key);
            return false;
        }
        catch (JSDisconnectedException)
        {
            _logger.LogDebug("SetLocalStorage: JSDisconnectedException for {Key} (circuit lost)", key);
            return false;
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("SetLocalStorage: ObjectDisposedException for {Key} (disposed)", key);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SetLocalStorage: Unexpected exception for {Key}", key);
            return false;
        }
    }
}

