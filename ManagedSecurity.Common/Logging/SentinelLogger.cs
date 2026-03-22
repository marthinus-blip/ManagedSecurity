using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ManagedSecurity.Common.Logging;

/// <summary>
/// Sentinel Logger: High-performance, NativeAOT-compatible logger with AiThoughts support.
/// Follows the Aesthetic of Verifiable Truth.
/// </summary>
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public static partial class SentinelLogger
{
    private static ILoggerFactory? _factory;
    private static ILogger _defaultLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public static void Initialize(ILoggerFactory factory)
    {
        _factory = factory;
        _defaultLogger = factory.CreateLogger("Sentinel");
    }

    /// <summary>
    /// Logs an AI Thought - reasoning that explains "Why" a specific implementation was chosen.
    /// This is the lowest level of logging, below Trace.
    /// </summary>
    public static void AiThought(string topic, string reasoning, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        // We use EventId 777 for AI Thoughts (Lucky No. 7 for ground truth)
        // Map to Trace level but with a specific prefix.
        LogAiThought(_defaultLogger, topic, reasoning, file, line);
    }

    [LoggerMessage(
        EventId = 777,
        Level = LogLevel.Trace,
        Message = "[AiThought] [{topic}] ({file}:{line}) {reasoning}")]
    private static partial void LogAiThought(ILogger? logger, string topic, string reasoning, string file, int line);

    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Information,
        Message = "[HEARTBEAT] {component} {state}")]
    public static partial void Heartbeat(ILogger logger, string component, string state);

    [LoggerMessage(
        EventId = 500,
        Level = LogLevel.Error,
        Message = "[NO SIGNAL] {component} Failure: {error}")]
    public static partial void NoSignal(ILogger logger, string component, string error);

    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "[INFO] {message}")]
    public static partial void Info(ILogger logger, string message);

    [LoggerMessage(EventId = 201, Level = LogLevel.Debug, Message = "[DEBUG] {message}")]
    public static partial void Debug(ILogger logger, string message);

    [LoggerMessage(EventId = 202, Level = LogLevel.Warning, Message = "[WARN] {message}")]
    public static partial void Warning(ILogger logger, string message);

    [LoggerMessage(EventId = 203, Level = LogLevel.Error, Message = "[ERROR] {message}")]
    public static partial void Error(ILogger logger, Exception? exception, string message);

    [LoggerMessage(EventId = 204, Level = LogLevel.Error, Message = "[ERROR] {message}")]
    public static partial void ErrorPlain(ILogger logger, string message);
    
    public static ILogger CreateLogger<T>() => _factory?.CreateLogger<T>() ?? _defaultLogger!;
    public static ILogger CreateLogger(string category) => _factory?.CreateLogger(category) ?? _defaultLogger!;
}
