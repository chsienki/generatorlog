using Microsoft.Diagnostics.Tracing;

namespace GeneratorLog.Analyze;

/// <summary>
/// Processes ETW events from the Microsoft-CodeAnalysis-General provider
/// and reconstructs generator execution data.
/// Adapted from https://github.com/chsienki/generatoretwviewer
/// </summary>
public class EventProcessor
{
    public const string CodeAnalysisEtwName = "Microsoft-CodeAnalysis-General";

    private static readonly StateTable MissingTable = new(-2, "", "<missing>");

    private readonly Dictionary<int, int> _executionIds = [];
    private readonly Dictionary<int, ProcessInfo> _processInfo = [];
    private readonly Dictionary<(int processId, int threadId), List<Transform>> _currentExecutions = [];
    private readonly Dictionary<(int processId, int threadId), int> _currentRunId = [];

    public List<ProcessInfo> ProcessInfo => [.. _processInfo.Values];

    /// <summary>
    /// Process a real TraceEvent from an ETW session or ETL file.
    /// </summary>
    public void ProcessEvent(TraceEvent e)
    {
        ProcessEvent(new GeneratorEventData(
            e.ProviderName,
            e.EventName,
            e.ProcessID,
            e.ProcessName,
            e.ThreadID,
            e.TimeStamp,
            e.TimeStampRelativeMSec,
            new TraceEventPayload(e)));
    }

    /// <summary>
    /// Process an event from a GeneratorEventData record (testable without real ETW).
    /// </summary>
    public void ProcessEvent(GeneratorEventData e)
    {
        if (e.ProviderName != CodeAnalysisEtwName)
            return;

        switch (e.EventName)
        {
            case "GeneratorDriverRunTime/Start":
                EnsureProcessSlot(e.ProcessId, e.ProcessName);
                _currentRunId[(e.ProcessId, e.ThreadId)] = _executionIds[e.ProcessId]++;
                break;

            case "GeneratorDriverRunTime/Stop":
                if (_processInfo.ContainsKey(e.ProcessId))
                {
                    _processInfo[e.ProcessId] = _processInfo[e.ProcessId] with
                    {
                        TotalExecutions = _processInfo[e.ProcessId].TotalExecutions + 1
                    };
                }
                break;

            case "SingleGeneratorRunTime/Start":
                _currentExecutions[(e.ProcessId, e.ThreadId)] = [GenerateStartPlaceholder(e)];
                break;

            case "SingleGeneratorRunTime/Stop":
                RecordGeneratorExecution(e);
                break;

            case "BuildStateTable":
                RecordStateTable(e);
                break;
        }
    }

    private void EnsureProcessSlot(int processId, string processName)
    {
        if (!_processInfo.ContainsKey(processId))
        {
            var name = string.IsNullOrWhiteSpace(processName) ? $"Process ({processId})" : $"{processName} ({processId})";
            _processInfo[processId] = new ProcessInfo(name, [], [], 1);
        }

        if (!_executionIds.ContainsKey(processId))
        {
            _executionIds[processId] = 0;
        }
    }

    private bool IsMissingExecutions(GeneratorEventData data) =>
        !_currentExecutions.ContainsKey((data.ProcessId, data.ThreadId))
        || !_processInfo.ContainsKey(data.ProcessId)
        || !_currentRunId.ContainsKey((data.ProcessId, data.ThreadId));

    private void RecordGeneratorExecution(GeneratorEventData data)
    {
        if (IsMissingExecutions(data))
            return;

        var transforms = _currentExecutions[(data.ProcessId, data.ThreadId)].Skip(1).ToList();
        var runId = _currentRunId[(data.ProcessId, data.ThreadId)];
        var processInfo = _processInfo[data.ProcessId];

        var generatorName = (string)data.Payload["generatorName"];
        var projectName = (string)(data.Payload.GetValueOrDefault("projectName") ?? "<unknown project>");
        var assemblyPath = (string)data.Payload["assemblyPath"];
        var elapsedTime = TimeSpan.FromTicks((long)data.Payload["elapsedTicks"]);
        var eventTime = ToEventTime(data, elapsedTime);

        var info = processInfo.Generators.SingleOrDefault(i => i.Name == generatorName && i.Assembly == assemblyPath);
        if (info is null)
        {
            info = new GeneratorInfo(generatorName, assemblyPath, []);
            processInfo.Generators.Add(info);
        }

        info.Executions.Add(new GeneratorRun(runId, projectName, eventTime, transforms));
    }

    private void RecordStateTable(GeneratorEventData data)
    {
        if (IsMissingExecutions(data))
            return;

        var previousTransform = _currentExecutions[(data.ProcessId, data.ThreadId)].Last();
        var time = ToEventTime(data, FromMS(data.TimeStampRelativeMSec).Subtract(previousTransform.Time.RelativeEnd));

        var previousTableId = (int)data.Payload["previousTable"];
        var newTableId = (int)data.Payload["newTable"];
        var tableType = (string)data.Payload["tableType"];

        var tables = _processInfo[data.ProcessId].Tables;

        if (!tables.TryGetValue(previousTableId, out var previousTable))
        {
            previousTable = tables[previousTableId] = new StateTable(previousTableId, (string)data.Payload["previousTableContent"], tableType);
        }

        if (!tables.TryGetValue(newTableId, out var newTable))
        {
            newTable = tables[newTableId] = new StateTable(newTableId, (string)data.Payload["newTableContent"], tableType);
        }

        if (newTable.Id == -1)
        {
            newTable = AsCached(previousTable);
        }

        var input1Id = (int)data.Payload["input1"];
        var input2Id = (int)data.Payload["input2"];

        tables.TryGetValue(input1Id, out var input1Table);
        input1Table ??= MissingTable;

        StateTable? input2Table = null;
        if (input2Id != -1)
        {
            tables.TryGetValue(input2Id, out input2Table);
            input2Table ??= MissingTable;
        }

        var transform = new Transform(
            (int)data.Payload["nodeHashCode"],
            (string)data.Payload["name"],
            time,
            previousTable,
            newTable,
            input1Table,
            input2Table);

        _currentExecutions[(data.ProcessId, data.ThreadId)].Add(transform);
    }

    private Transform GenerateStartPlaceholder(GeneratorEventData e) =>
        new(-1, "GeneratorStart", ToEventTime(e, TimeSpan.Zero), MissingTable, MissingTable, MissingTable, MissingTable);

    internal static StateTable AsCached(StateTable table)
    {
        var cached = string.Create(table.Content.Length, table.Content, (span, content) =>
        {
            for (int i = 0; i < content.Length; i++)
            {
                span[i] = content[i] is 'A' or 'C' or 'M' ? 'C' : content[i];
            }
        });
        return table with { Content = cached };
    }

    private static TimeSpan FromMS(double ms) => TimeSpan.FromTicks((long)(ms * 10_000));

    private static EventTime ToEventTime(GeneratorEventData data, TimeSpan duration)
    {
        return new EventTime(
            Start: data.TimeStamp.Subtract(duration),
            End: data.TimeStamp,
            RelativeStart: FromMS(data.TimeStampRelativeMSec).Subtract(duration),
            RelativeEnd: FromMS(data.TimeStampRelativeMSec),
            Duration: duration);
    }

    /// <summary>
    /// Adapter that wraps TraceEvent.PayloadByName into an IReadOnlyDictionary interface.
    /// </summary>
    private sealed class TraceEventPayload(TraceEvent e) : IReadOnlyDictionary<string, object>
    {
        public object this[string key] => e.PayloadByName(key);
        public IEnumerable<string> Keys => e.PayloadNames;
        public IEnumerable<object> Values => e.PayloadNames.Select(n => e.PayloadByName(n));
        public int Count => e.PayloadNames.Length;
        public bool ContainsKey(string key) => e.PayloadNames.Contains(key);
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() =>
            e.PayloadNames.Select(n => new KeyValuePair<string, object>(n, e.PayloadByName(n))).GetEnumerator();
        public bool TryGetValue(string key, out object value)
        {
            if (e.PayloadNames.Contains(key)) { value = e.PayloadByName(key); return true; }
            value = default!; return false;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
