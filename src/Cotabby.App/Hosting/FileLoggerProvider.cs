using System.IO;
using Microsoft.Extensions.Logging;

namespace Cotabby.App.Hosting;

/// <summary>
/// Append-only single-file logger, so diagnostics land at a known path without redirecting
/// stdout. There is no rotation and no batching. Every call writes and flushes under one lock,
/// which keeps lines from different threads whole.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 5 * 1024 * 1024;

    private readonly object _gate = new();
    private StreamWriter? _writer;

    internal FileLoggerProvider(StreamWriter writer) => _writer = writer;

    public FileLoggerProvider(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // The log is for diagnosing the current session, so it starts over once it has grown large.
        if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
        _writer = new StreamWriter(new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read))
        { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        lock (_gate)
        {
            var writer = _writer;
            _writer = null;
            writer?.Dispose();
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLoggerProvider _owner;

        public FileLogger(string category, FileLoggerProvider owner)
        {
            _category = category;
            _owner = owner;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => _owner._writer is not null && logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTime.Now:HH:mm:ss.fff} {ShortLevel(logLevel)}: {_category}[{eventId.Id}] {formatter(state, exception)}";
            if (exception is not null) line += $" :: {exception}";
            lock (_owner._gate)
            {
                // Restart/shutdown failures may be reported after DI retired its log provider.
                // Resolve the live writer under the same gate as Dispose rather than retaining
                // a closed StreamWriter in every logger.
                _owner._writer?.WriteLine(line);
            }
        }

        private static string ShortLevel(LogLevel l) => l switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "info",
        };
    }
}
