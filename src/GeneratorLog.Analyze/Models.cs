namespace GeneratorLog.Analyze;

public record EventTime(DateTime Start, DateTime End, TimeSpan RelativeStart, TimeSpan RelativeEnd, TimeSpan Duration);

public record StateTable(int Id, string Content, string Type);

public record Transform(int NodeHashCode, string Name, EventTime Time, StateTable PreviousTable, StateTable NewTable, StateTable Input1, StateTable? Input2);

public record GeneratorRun(int DriverRun, string ProjectName, EventTime Time, List<Transform> Transforms);

public record GeneratorInfo(string Name, string Assembly, List<GeneratorRun> Executions);

public record ProcessInfo(string Name, List<GeneratorInfo> Generators, Dictionary<int, StateTable> Tables, int TotalExecutions);
