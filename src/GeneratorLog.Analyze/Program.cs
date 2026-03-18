using System.Text;
using GeneratorLog.Analyze;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Spectre.Console;

var files = new List<FileInfo>();
string? csvPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--csv" or "-c" && i + 1 < args.Length)
    {
        csvPath = args[++i];
    }
    else if (args[i] is "--help" or "-h" or "-?")
    {
        Console.WriteLine("Usage: generatorlog-analyze [--csv|-c <path>] <file1.etl> [file2.nettrace ...]");
        Console.WriteLine();
        Console.WriteLine("Analyze Roslyn source generator events from ETL or nettrace files.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --csv, -c <path>  Export results as CSV to the specified file.");
        return 0;
    }
    else
    {
        files.Add(new FileInfo(args[i]));
    }
}

if (files.Count == 0)
{
    Console.Error.WriteLine("Error: No trace files specified. Use --help for usage.");
    return 1;
}

return Run(files, csvPath is not null ? new FileInfo(csvPath) : null);

static int Run(List<FileInfo> files, FileInfo? csvFile)
{
    foreach (var file in files)
    {
        if (!file.Exists)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {file.FullName}");
            return 1;
        }
    }

    var allProcessInfo = new List<ProcessInfo>();

    foreach (var file in files)
    {
        AnsiConsole.MarkupLine($"[blue]Processing:[/] {file.Name}");

        var processor = new EventProcessor();

        ProcessTraceFile(file, processor);

        allProcessInfo.AddRange(processor.ProcessInfo);
    }

    if (allProcessInfo.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No generator events found in the provided ETL file(s).[/]");
        AnsiConsole.MarkupLine("Ensure the trace was captured with the [bold]Microsoft-CodeAnalysis-General[/] ETW provider enabled.");
        return 0;
    }

    RenderSummary(allProcessInfo);
    RenderPerProcessTable(allProcessInfo);
    RenderPerGeneratorTable(allProcessInfo);

    if (csvFile is not null)
    {
        CsvExporter.ExportToFile(allProcessInfo, csvFile.FullName);
        AnsiConsole.MarkupLine($"[green]CSV exported to:[/] {csvFile.FullName}");
    }

    return 0;
}

static void RenderSummary(List<ProcessInfo> processes)
{
    var totalDriverRuns = processes.Sum(p => p.TotalExecutions);
    var totalGeneratorExecutions = processes.Sum(p => p.Generators.Sum(g => g.Executions.Count));
    var totalGenerators = processes.SelectMany(p => p.Generators).DistinctBy(g => (g.Name, g.Assembly)).Count();

    AnsiConsole.WriteLine();
    var panel = new Panel(
        $"""
        [bold]Total driver runs:[/]          {totalDriverRuns}
        [bold]Total generator executions:[/] {totalGeneratorExecutions}
        [bold]Unique generators:[/]          {totalGenerators}
        [bold]Processes:[/]                  {processes.Count}
        """)
    {
        Header = new PanelHeader("[bold green] Summary [/]"),
        Border = BoxBorder.Rounded,
        Padding = new Padding(2, 1)
    };
    AnsiConsole.Write(panel);
}

static void RenderPerProcessTable(List<ProcessInfo> processes)
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold underline]Per-Process Overview[/]");
    AnsiConsole.WriteLine();

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("Process")
        .AddColumn(new TableColumn("Driver Runs").RightAligned())
        .AddColumn(new TableColumn("Generators").RightAligned())
        .AddColumn(new TableColumn("Total Executions").RightAligned());

    foreach (var process in processes)
    {
        table.AddRow(
            process.Name,
            process.TotalExecutions.ToString(),
            process.Generators.Count.ToString(),
            process.Generators.Sum(g => g.Executions.Count).ToString());
    }

    AnsiConsole.Write(table);
}

static void RenderPerGeneratorTable(List<ProcessInfo> processes)
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold underline]Per-Generator Statistics[/]");

    foreach (var process in processes)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(process.Name)}[/]");

        if (process.Generators.Count == 0)
        {
            AnsiConsole.MarkupLine("  [dim]No generator executions recorded.[/]");
            continue;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Generator")
            .AddColumn("Assembly")
            .AddColumn(new TableColumn("Count").RightAligned())
            .AddColumn(new TableColumn("Min (ms)").RightAligned())
            .AddColumn(new TableColumn("Avg (ms)").RightAligned())
            .AddColumn(new TableColumn("Max (ms)").RightAligned())
            .AddColumn(new TableColumn("Cumulative").RightAligned())
            .AddColumn("Projects");

        foreach (var gen in process.Generators.OrderBy(g => g.Name))
        {
            if (gen.Executions.Count == 0)
                continue;

            var times = gen.Executions.Select(e => e.Time.Duration.TotalMilliseconds).ToList();
            var projects = gen.Executions.Select(e => e.ProjectName).Distinct().OrderBy(p => p);

            table.AddRow(
                Markup.Escape(gen.Name),
                Markup.Escape(CsvExporter.ShortenAssemblyPath(gen.Assembly)),
                gen.Executions.Count.ToString(),
                times.Min().ToString("F2"),
                times.Average().ToString("F2"),
                times.Max().ToString("F2"),
                TimeSpan.FromMilliseconds(times.Sum()).ToString(@"hh\:mm\:ss\.fff"),
                Markup.Escape(string.Join(", ", projects)));
        }

        AnsiConsole.Write(table);
    }
}

static void ProcessTraceFile(FileInfo file, EventProcessor processor)
{
    var extension = file.Extension.ToLowerInvariant();
    if (extension == ".nettrace")
    {
        using var source = new EventPipeEventSource(file.FullName);
        source.Dynamic.AddCallbackForProviderEvents(
            (providerName, eventName) => providerName == EventProcessor.CodeAnalysisEtwName
                ? EventFilterResponse.AcceptEvent
                : EventFilterResponse.RejectProvider,
            processor.ProcessEvent);
        source.Process();
    }
    else
    {
        // .etl or .etlx
        using var traceLog = TraceLog.OpenOrConvert(file.FullName);
        foreach (var e in traceLog.Events)
        {
            if (e.ProviderName == EventProcessor.CodeAnalysisEtwName)
            {
                processor.ProcessEvent(e);
            }
        }
    }
}
