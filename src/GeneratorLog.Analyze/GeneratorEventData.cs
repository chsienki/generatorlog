namespace GeneratorLog.Analyze;

/// <summary>
/// Lightweight representation of a trace event for testability.
/// The EventProcessor can work with this directly, avoiding the need
/// for real TraceEvent instances in tests.
/// </summary>
public record GeneratorEventData(
    string ProviderName,
    string EventName,
    int ProcessId,
    string ProcessName,
    int ThreadId,
    DateTime TimeStamp,
    double TimeStampRelativeMSec,
    IReadOnlyDictionary<string, object> Payload);
