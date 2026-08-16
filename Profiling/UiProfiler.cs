using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using PackRat.Helpers;
using UnityEngine;

namespace PackRat.Profiling;

/// <summary>
/// Low-frequency, buffered UI trace writer for opt-in developer diagnostics.
/// Rows are pipe-delimited so captures remain readable as text and import cleanly into a spreadsheet.
/// </summary>
internal static class UiProfiler
{
    private const int FlushEveryEvents = 64;
    private static object _sync;
    private static Stopwatch _sessionClock;
    private static StreamWriter _writer;
    private static bool _enabled;
    private static bool _initializationFailed;
    private static int _eventsSinceFlush;
    private static long _nextFlushMilliseconds;

    internal static bool IsEnabled => _enabled;

    internal static string OutputPath { get; private set; }

    /// <summary>
    /// Applies the live Developer Profiler preference. The disabled path creates neither a file nor a directory.
    /// </summary>
    internal static void ApplyEnabledState(bool enabled)
    {
        if (enabled)
        {
            if (!_enabled && !_initializationFailed)
                Initialize();
            return;
        }

        if (_enabled || _initializationFailed)
            Shutdown();
    }

    internal static void Initialize()
    {
        if (_enabled || _initializationFailed)
            return;

        lock (GetSync())
        {
            if (_enabled || _initializationFailed)
                return;

            try
            {
                var directory = Path.Combine(Environment.CurrentDirectory, "UserData", "PackRatProfiler");
                Directory.CreateDirectory(directory);
#if MONO
                const string runtime = "mono";
#else
                const string runtime = "il2cpp";
#endif
                var outputPath = Path.Combine(directory,
                    $"packrat-ui-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{runtime}.log");
                var writer = new StreamWriter(new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.ReadWrite, 32768, FileOptions.SequentialScan), new UTF8Encoding(false), 32768);
                writer.WriteLine("utc|session_us|frame|thread|kind|feature|operation|duration_us|gc0|gc1|gc2|details");

                _sessionClock = Stopwatch.StartNew();
                _writer = writer;
                OutputPath = outputPath;
                _eventsSinceFlush = 0;
                _nextFlushMilliseconds = 1000;
                _enabled = true;
                WriteLocked("state", "session", "start", 0L, 0, 0, 0,
                    $"version={BuildInfo.Version};runtime={runtime};os={Environment.OSVersion};cpu={Environment.ProcessorCount}");
                writer.Flush();
                ModLogger.Info($"PackRat developer profiler trace: {OutputPath}");
            }
            catch (Exception ex)
            {
                _writer?.Dispose();
                _writer = null;
                _sessionClock?.Stop();
                _sessionClock = null;
                _enabled = false;
                _initializationFailed = true;
                ModLogger.Error("PackRat developer profiler could not create its trace file", ex);
            }
        }
    }

    internal static void Event(string feature, string operation, string details = null)
    {
        if (!_enabled)
            return;

        Write("state", feature, operation, 0L, 0, 0, 0, details);
    }

    internal static UiProfileScope Measure(string feature, string operation, string details = null)
    {
        return _enabled ? new UiProfileScope(feature, operation, details) : default;
    }

    internal static void Shutdown()
    {
        if (!_enabled && !_initializationFailed)
            return;

        lock (GetSync())
        {
            if (_writer != null)
            {
                WriteLocked("state", "session", "stop", 0L, 0, 0, 0, null);
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }

            _enabled = false;
            _eventsSinceFlush = 0;
            _nextFlushMilliseconds = 0;
            _sessionClock?.Stop();
            _sessionClock = null;
            _initializationFailed = false;
        }
    }

    internal static void FlushIfDue()
    {
        if (!_enabled || _sessionClock == null)
            return;

        var elapsed = _sessionClock.ElapsedMilliseconds;
        if (elapsed < _nextFlushMilliseconds)
            return;

        lock (GetSync())
        {
            if (!_enabled || _sessionClock == null || _sessionClock.ElapsedMilliseconds < _nextFlushMilliseconds)
                return;

            _writer?.Flush();
            _eventsSinceFlush = 0;
            _nextFlushMilliseconds = _sessionClock.ElapsedMilliseconds + 1000;
        }
    }

    internal static void Complete(string feature, string operation, long startedTimestamp,
        int gc0, int gc1, int gc2, string details)
    {
        if (!_enabled || startedTimestamp == 0)
            return;

        var durationUs = (Stopwatch.GetTimestamp() - startedTimestamp) * 1_000_000L / Stopwatch.Frequency;
        Write("span", feature, operation, durationUs,
            GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2, details);
    }

    private static object GetSync()
    {
        if (_sync != null)
            return _sync;

        var created = new object();
        return Interlocked.CompareExchange(ref _sync, created, null) ?? _sync;
    }

    private static void Write(string kind, string feature, string operation, long durationUs,
        int gc0, int gc1, int gc2, string details)
    {
        if (!_enabled)
            return;

        lock (GetSync())
        {
            if (!_enabled)
                return;

            WriteLocked(kind, feature, operation, durationUs, gc0, gc1, gc2, details);
            if (++_eventsSinceFlush >= FlushEveryEvents)
            {
                _writer?.Flush();
                _eventsSinceFlush = 0;
            }
        }
    }

    private static void WriteLocked(string kind, string feature, string operation, long durationUs,
        int gc0, int gc1, int gc2, string details)
    {
        if (_writer == null || _sessionClock == null)
            return;

        _writer.Write(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        _writer.Write('|');
        _writer.Write(_sessionClock.ElapsedTicks * 1_000_000L / Stopwatch.Frequency);
        _writer.Write('|');
        _writer.Write(SafeFrameCount());
        _writer.Write('|');
        _writer.Write(Thread.CurrentThread.ManagedThreadId);
        _writer.Write('|');
        _writer.Write(Sanitize(kind));
        _writer.Write('|');
        _writer.Write(Sanitize(feature));
        _writer.Write('|');
        _writer.Write(Sanitize(operation));
        _writer.Write('|');
        _writer.Write(durationUs);
        _writer.Write('|');
        _writer.Write(gc0);
        _writer.Write('|');
        _writer.Write(gc1);
        _writer.Write('|');
        _writer.Write(gc2);
        _writer.Write('|');
        _writer.WriteLine(Sanitize(details));
    }

    private static int SafeFrameCount()
    {
        try { return Time.frameCount; }
        catch { return -1; }
    }

    private static string Sanitize(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
    }
}

internal readonly struct UiProfileScope : IDisposable
{
    private readonly string _feature;
    private readonly string _operation;
    private readonly string _details;
    private readonly long _startedTimestamp;
    private readonly int _gc0;
    private readonly int _gc1;
    private readonly int _gc2;

    internal UiProfileScope(string feature, string operation, string details)
    {
        _feature = feature;
        _operation = operation;
        _details = details;
        _startedTimestamp = Stopwatch.GetTimestamp();
        _gc0 = GC.CollectionCount(0);
        _gc1 = GC.CollectionCount(1);
        _gc2 = GC.CollectionCount(2);
    }

    public void Dispose()
    {
        if (_startedTimestamp != 0)
            UiProfiler.Complete(_feature, _operation, _startedTimestamp, _gc0, _gc1, _gc2, _details);
    }
}
