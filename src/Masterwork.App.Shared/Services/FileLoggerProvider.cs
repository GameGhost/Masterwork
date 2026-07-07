using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// A minimal rolling-daily file logger — no external logging package, just enough to leave a trail
/// on disk for diagnosing crashes a user hits before they can describe them (see MAUI's first-launch
/// upload crash, masterwork-plan-rev14.md). Only usable where real file I/O exists: MAUI heads and
/// the <c>Masterwork.App.Web</c> ASP.NET Core host. Not registered for <c>Masterwork.App.Web.Client</c>
/// (WASM) — there's no filesystem in the browser sandbox; the browser console is the log there.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly object _writeLock = new();

    public FileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    private string CurrentLogFilePath => Path.Combine(_logDirectory, $"masterwork-{DateTime.UtcNow:yyyy-MM-dd}.log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void WriteLine(string line)
    {
        lock (_writeLock)
        {
            File.AppendAllText(CurrentLogFilePath, line + Environment.NewLine);
        }
    }

    public void Dispose()
    {
    }

    private sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {categoryName}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            provider.WriteLine(line);
        }
    }
}

/// <summary>Registration helper for <see cref="FileLoggerProvider"/>.</summary>
public static class FileLoggingExtensions
{
    /// <summary>Adds a rolling-daily file logger writing into <paramref name="logDirectory"/>.</summary>
    public static ILoggingBuilder AddMasterworkFileLogger(this ILoggingBuilder builder, string logDirectory)
    {
        // Registering the provider instance directly via Services (rather than the usual
        // AddProvider(ILoggerProvider) extension) avoids needing a PackageReference to the full
        // Microsoft.Extensions.Logging package — ILoggingBuilder.Services is already part of
        // Microsoft.Extensions.Logging.Abstractions, which this project references transitively.
        builder.Services.AddSingleton<ILoggerProvider>(new FileLoggerProvider(logDirectory));
        return builder;
    }
}
