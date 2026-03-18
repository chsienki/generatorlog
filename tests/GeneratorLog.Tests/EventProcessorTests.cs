using GeneratorLog.Analyze;

namespace GeneratorLog.Tests;

public class EventProcessorTests
{
    private static readonly DateTime BaseTime = new(2026, 3, 18, 12, 0, 0, DateTimeKind.Utc);
    private const int Pid = 1234;
    private const int Tid = 5;
    private const string Provider = EventProcessor.CodeAnalysisEtwName;

    [Fact]
    public void IgnoresEventsFromOtherProviders()
    {
        var processor = new EventProcessor();

        processor.ProcessEvent(MakeEvent("SomeOtherProvider", "GeneratorDriverRunTime/Start", 100.0));

        Assert.Empty(processor.ProcessInfo);
    }

    [Fact]
    public void DriverStartStop_CountsTotalExecutions()
    {
        var processor = new EventProcessor();

        processor.ProcessEvent(MakeDriverStart(100.0));
        processor.ProcessEvent(MakeDriverStop(200.0));

        var processes = processor.ProcessInfo;
        Assert.Single(processes);
        Assert.Equal(2, processes[0].TotalExecutions); // 1 initial + 1 from stop
    }

    [Fact]
    public void MultipleDriverRuns_CountsCorrectly()
    {
        var processor = new EventProcessor();

        processor.ProcessEvent(MakeDriverStart(100.0));
        processor.ProcessEvent(MakeDriverStop(200.0));
        processor.ProcessEvent(MakeDriverStart(300.0));
        processor.ProcessEvent(MakeDriverStop(400.0));
        processor.ProcessEvent(MakeDriverStart(500.0));
        processor.ProcessEvent(MakeDriverStop(600.0));

        var processes = processor.ProcessInfo;
        Assert.Single(processes);
        Assert.Equal(4, processes[0].TotalExecutions); // 1 initial + 3 stops
    }

    [Fact]
    public void SingleGeneratorRun_RecordsExecution()
    {
        var processor = new EventProcessor();
        long ticks = TimeSpan.FromMilliseconds(42).Ticks;

        processor.ProcessEvent(MakeDriverStart(100.0));
        processor.ProcessEvent(MakeGeneratorStart("TestGen", 150.0));
        processor.ProcessEvent(MakeGeneratorStop("TestGen", "TestAssembly.dll", "MyProject", ticks, 200.0));
        processor.ProcessEvent(MakeDriverStop(250.0));

        var processes = processor.ProcessInfo;
        Assert.Single(processes);
        Assert.Single(processes[0].Generators);

        var gen = processes[0].Generators[0];
        Assert.Equal("TestGen", gen.Name);
        Assert.Equal("TestAssembly.dll", gen.Assembly);
        Assert.Single(gen.Executions);
        Assert.Equal("MyProject", gen.Executions[0].ProjectName);
        Assert.Equal(TimeSpan.FromTicks(ticks), gen.Executions[0].Time.Duration);
    }

    [Fact]
    public void MultipleGenerators_GroupedCorrectly()
    {
        var processor = new EventProcessor();
        long ticks = TimeSpan.FromMilliseconds(10).Ticks;

        processor.ProcessEvent(MakeDriverStart(100.0));
        processor.ProcessEvent(MakeGeneratorStart("GenA", 110.0));
        processor.ProcessEvent(MakeGeneratorStop("GenA", "A.dll", "Proj", ticks, 120.0));
        processor.ProcessEvent(MakeGeneratorStart("GenB", 130.0));
        processor.ProcessEvent(MakeGeneratorStop("GenB", "B.dll", "Proj", ticks, 140.0));
        processor.ProcessEvent(MakeDriverStop(150.0));

        var processes = processor.ProcessInfo;
        Assert.Single(processes);
        Assert.Equal(2, processes[0].Generators.Count);
        Assert.Equal("GenA", processes[0].Generators[0].Name);
        Assert.Equal("GenB", processes[0].Generators[1].Name);
    }

    [Fact]
    public void SameGeneratorMultipleRuns_AccumulatesExecutions()
    {
        var processor = new EventProcessor();
        long ticks = TimeSpan.FromMilliseconds(10).Ticks;

        // First driver run
        processor.ProcessEvent(MakeDriverStart(100.0));
        processor.ProcessEvent(MakeGeneratorStart("GenA", 110.0));
        processor.ProcessEvent(MakeGeneratorStop("GenA", "A.dll", "Proj", ticks, 120.0));
        processor.ProcessEvent(MakeDriverStop(130.0));

        // Second driver run
        processor.ProcessEvent(MakeDriverStart(200.0));
        processor.ProcessEvent(MakeGeneratorStart("GenA", 210.0));
        processor.ProcessEvent(MakeGeneratorStop("GenA", "A.dll", "Proj", ticks * 2, 230.0));
        processor.ProcessEvent(MakeDriverStop(240.0));

        var gen = processor.ProcessInfo[0].Generators.Single();
        Assert.Equal(2, gen.Executions.Count);
        Assert.Equal(0, gen.Executions[0].DriverRun);
        Assert.Equal(1, gen.Executions[1].DriverRun);
    }

    [Fact]
    public void MultipleProcesses_TrackedSeparately()
    {
        var processor = new EventProcessor();
        long ticks = TimeSpan.FromMilliseconds(5).Ticks;

        // Process A
        processor.ProcessEvent(MakeDriverStart(100.0, pid: 100, processName: "dotnet"));
        processor.ProcessEvent(MakeGeneratorStart("Gen", 110.0, pid: 100));
        processor.ProcessEvent(MakeGeneratorStop("Gen", "G.dll", "P", ticks, 120.0, pid: 100));
        processor.ProcessEvent(MakeDriverStop(130.0, pid: 100));

        // Process B
        processor.ProcessEvent(MakeDriverStart(100.0, pid: 200, processName: "csc"));
        processor.ProcessEvent(MakeGeneratorStart("Gen", 110.0, pid: 200));
        processor.ProcessEvent(MakeGeneratorStop("Gen", "G.dll", "P", ticks, 120.0, pid: 200));
        processor.ProcessEvent(MakeDriverStop(130.0, pid: 200));

        Assert.Equal(2, processor.ProcessInfo.Count);
        Assert.Contains(processor.ProcessInfo, p => p.Name.Contains("dotnet"));
        Assert.Contains(processor.ProcessInfo, p => p.Name.Contains("csc"));
    }

    [Fact]
    public void StopWithoutStart_DropsIncompleteRun()
    {
        var processor = new EventProcessor();
        long ticks = TimeSpan.FromMilliseconds(10).Ticks;

        // Generator stop without a start — should be silently dropped
        processor.ProcessEvent(MakeGeneratorStop("GenA", "A.dll", "Proj", ticks, 50.0));

        Assert.Empty(processor.ProcessInfo);
    }

    [Fact]
    public void BuildStateTable_RecordsTransform()
    {
        var processor = new EventProcessor();
        long ticks = TimeSpan.FromMilliseconds(20).Ticks;

        processor.ProcessEvent(MakeDriverStart(100.0));
        processor.ProcessEvent(MakeGeneratorStart("Gen", 110.0));
        processor.ProcessEvent(MakeBuildStateTable(
            previousTable: 1, previousTableContent: "A",
            newTable: 2, newTableContent: "M",
            tableType: "SyntaxTrees",
            input1: 0, input2: -1,
            nodeHashCode: 42, name: "Transform1",
            relativeMs: 115.0));
        processor.ProcessEvent(MakeGeneratorStop("Gen", "G.dll", "P", ticks, 120.0));
        processor.ProcessEvent(MakeDriverStop(130.0));

        var gen = processor.ProcessInfo[0].Generators[0];
        Assert.Single(gen.Executions);
        Assert.Single(gen.Executions[0].Transforms);
        Assert.Equal("Transform1", gen.Executions[0].Transforms[0].Name);
    }

    [Fact]
    public void AsCached_ReplacesAddedAndModifiedWithCached()
    {
        var table = new StateTable(1, "ACMR", "type");
        var cached = EventProcessor.AsCached(table);

        Assert.Equal("CCCR", cached.Content);
    }

    // --- Helper methods for creating synthetic events ---

    private static GeneratorEventData MakeEvent(string provider, string eventName, double relativeMs,
        int pid = Pid, int tid = Tid, string processName = "dotnet",
        Dictionary<string, object>? payload = null)
    {
        return new GeneratorEventData(
            provider,
            eventName,
            pid,
            processName,
            tid,
            BaseTime.AddMilliseconds(relativeMs),
            relativeMs,
            payload ?? new Dictionary<string, object>());
    }

    private static GeneratorEventData MakeDriverStart(double relativeMs, int pid = Pid, string processName = "dotnet")
        => MakeEvent(Provider, "GeneratorDriverRunTime/Start", relativeMs, pid: pid, processName: processName);

    private static GeneratorEventData MakeDriverStop(double relativeMs, int pid = Pid)
        => MakeEvent(Provider, "GeneratorDriverRunTime/Stop", relativeMs, pid: pid);

    private static GeneratorEventData MakeGeneratorStart(string name, double relativeMs, int pid = Pid)
        => MakeEvent(Provider, "SingleGeneratorRunTime/Start", relativeMs, pid: pid,
            payload: new Dictionary<string, object> { ["generatorName"] = name });

    private static GeneratorEventData MakeGeneratorStop(string name, string assembly, string project,
        long elapsedTicks, double relativeMs, int pid = Pid)
        => MakeEvent(Provider, "SingleGeneratorRunTime/Stop", relativeMs, pid: pid,
            payload: new Dictionary<string, object>
            {
                ["generatorName"] = name,
                ["assemblyPath"] = assembly,
                ["projectName"] = project,
                ["elapsedTicks"] = elapsedTicks
            });

    private static GeneratorEventData MakeBuildStateTable(
        int previousTable, string previousTableContent,
        int newTable, string newTableContent,
        string tableType, int input1, int input2,
        int nodeHashCode, string name, double relativeMs, int pid = Pid)
        => MakeEvent(Provider, "BuildStateTable", relativeMs, pid: pid,
            payload: new Dictionary<string, object>
            {
                ["previousTable"] = previousTable,
                ["previousTableContent"] = previousTableContent,
                ["newTable"] = newTable,
                ["newTableContent"] = newTableContent,
                ["tableType"] = tableType,
                ["input1"] = input1,
                ["input2"] = input2,
                ["nodeHashCode"] = nodeHashCode,
                ["name"] = name
            });
}
