using System;
using MelonLoader;

namespace PackRat.Helpers;

/// <summary>
/// Centralized logging utility for PackRat mod.
/// Provides consistent logging across Mono and IL2CPP runtimes.
/// </summary>
public static class ModLogger
{
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
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Debug(string message)
    {
#if RELEASE
#else
        MelonLogger.Msg($"[DEBUG] {message}");
#endif
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
