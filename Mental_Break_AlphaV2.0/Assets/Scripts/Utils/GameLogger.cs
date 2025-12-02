using UnityEngine;

/// <summary>
/// Conditional logging wrapper to reduce performance impact in release builds.
/// Methods are stripped from non-development builds via [Conditional] attribute.
/// </summary>
public static class GameLogger
{
    /// <summary>
    /// Log a message. Only included in Editor and Development builds.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void Log(string message)
    {
        Debug.Log(message);
    }
    
    /// <summary>
    /// Log a message with context object. Only included in Editor and Development builds.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void Log(string message, Object context)
    {
        Debug.Log(message, context);
    }
    
    /// <summary>
    /// Log a warning. Included in all builds (warnings are important).
    /// </summary>
    public static void LogWarning(string message)
    {
        Debug.LogWarning(message);
    }
    
    /// <summary>
    /// Log a warning with context. Included in all builds.
    /// </summary>
    public static void LogWarning(string message, Object context)
    {
        Debug.LogWarning(message, context);
    }
    
    /// <summary>
    /// Log an error. Included in all builds (errors are critical).
    /// </summary>
    public static void LogError(string message)
    {
        Debug.LogError(message);
    }
    
    /// <summary>
    /// Log an error with context. Included in all builds.
    /// </summary>
    public static void LogError(string message, Object context)
    {
        Debug.LogError(message, context);
    }
    
    /// <summary>
    /// Verbose logging for debugging asset loading. Only in Editor/Development.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void LogAsset(string message)
    {
        Debug.Log($"[Asset] {message}");
    }
    
    /// <summary>
    /// Verbose logging for debugging commands. Only in Editor/Development.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void LogCommand(string message)
    {
        Debug.Log($"[Command] {message}");
    }
}

