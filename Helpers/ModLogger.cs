using System;
using MelonLoader;

namespace PackRat.Helpers;

/// <summary>
/// Centralized logging utility for PackRat mod.
/// Provides consistent logging across Mono and IL2CPP runtimes.
/// </summary>
public static class ModLogger
{
    private static bool _debugLoggingEnabled;
    private static bool _syncDebugLoggingEnabled;

    /// <summary>
    /// Enables or disables verbose PackRat diagnostics at runtime. Release builds keep this
    /// disabled by default, but users can turn it on from the backpack settings when collecting
    /// a log for support.
    /// </summary>
    public static void SetDebugLoggingEnabled(bool enabled)
    {
        _debugLoggingEnabled = enabled;
    }

    /// <summary>
    /// Enables or disables verbose backpack synchronization diagnostics at runtime.
    /// This channel remains independent from general release diagnostics.
    /// </summary>
    public static void SetSyncDebugLoggingEnabled(bool enabled)
    {
        _syncDebugLoggingEnabled = enabled;
    }

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Info(string message)
    {
        MelonLogger.Msg(message);
    }

    /// <summary>
    /// Logs a debug message. Only outputs in Debug builds.
    /// In Release builds it outputs only while the PackRat debug-logging preference is enabled.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Debug(string message)
    {
#if RELEASE
        if (!_debugLoggingEnabled)
            return;
        MelonLogger.Msg($"[DEBUG] {message}");
#else
        MelonLogger.Msg($"[DEBUG] {message}");
#endif
    }

    /// <summary>
    /// Logs a backpack synchronization diagnostic. In Release builds it outputs only while the
    /// dedicated sync-diagnostics preference is enabled.
    /// </summary>
    /// <param name="message">The synchronization diagnostic to log.</param>
    public static void SyncDebug(string message)
    {
#if RELEASE
        if (!_syncDebugLoggingEnabled)
            return;
#endif
        MelonLogger.Msg($"[SYNC DEBUG] {message}");
    }

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Warn(string message)
    {
        MelonLogger.Warning(message);
    }

    /// <summary>
    /// Logs an error message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Error(string message)
    {
        MelonLogger.Error(message);
    }

    /// <summary>
    /// Logs an error message with exception details.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">The exception to log.</param>
    public static void Error(string message, Exception exception)
    {
        MelonLogger.Error($"{message}: {exception.Message}");
        MelonLogger.Error($"Stack trace: {exception.StackTrace}");
    }
}
