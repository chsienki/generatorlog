using System.IO.Compression;
using System.Text;
using GeneratorLog.Analyze;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Spectre.Console;

var files = new List<FileInfo>();
string? csvPath = null;
bool verbose = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--csv" or "-c" && i + 1 < args.Length)
    {
        csvPath = args[++i];
    }
    else if (args[i] is "--verbose" or "-v")
    {
        verbose = true;
    }
    else if (args[i] is "--help" or "-h" or "-?")
    {
        Console.WriteLine("Usage: generatorlog-analyze [options] <file1.etl> [file2.nettrace ...]");
        Console.WriteLine();
        Console.WriteLine("Analyze Roslyn source generator events from trace files.");
        Console.WriteLine();
        Console.WriteLine("Supported formats: .etl, .etlx, .nettrace, .nettrace.zip, .etl.zip");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --csv, -c <path>   Export results as CSV to the specified file.");
        Console.WriteLine("  --verbose, -v      Show detailed diagnostic output.");
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

return Run(files, csvPath is not null ? new FileInfo(csvPath) : null, verbose);

static int Run(List<FileInfo> files, FileInfo? csvFile, bool verbose)
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
    int razorEventCount = 0;

    foreach (var file in files)
    {
        AnsiConsole.MarkupLine($"[blue]Processing:[/] {file.Name}");
        LogVerbose(verbose, $"Opening file: {file.FullName} ({file.Length / 1024.0:F1} KB)");
        LogVerbose(verbose, $"File extension: {file.Extension}");

        var processor = new EventProcessor();
        int fileRazorEvents = 0;

        ProcessTraceFile(file, processor, verbose, ref fileRazorEvents);

        var info = processor.ProcessInfo;
        LogVerbose(verbose, $"Found {info.Count} process(es), {info.Sum(p => p.Generators.Count)} generator(s), {info.Sum(p => p.Generators.Sum(g => g.Executions.Count))} execution(s)");
        if (fileRazorEvents > 0)
            LogVerbose(verbose, $"Found {fileRazorEvents} Razor source generator event(s)");
        allProcessInfo.AddRange(info);
        razorEventCount += fileRazorEvents;
    }

    if (allProcessInfo.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No generator events found in the provided trace file(s).[/]");
        AnsiConsole.MarkupLine("Ensure the trace was captured with the [bold]Microsoft-CodeAnalysis-General[/] ETW provider enabled.");
        return 0;
    }

    RenderSummary(allProcessInfo);
    RenderPerProcessTable(allProcessInfo);
    RenderPerGeneratorTable(allProcessInfo);

    if (razorEventCount > 0)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[blue]Note:[/] This trace also contains [bold]{razorEventCount}[/] Razor source generator event(s) ([dim]Microsoft-DotNet-SDK-Razor-SourceGenerator[/]).");
        AnsiConsole.MarkupLine("[dim]These can be analyzed with PerfView or other ETW tools for detailed Razor diagnostics.[/]");
    }

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

static void ProcessTraceFile(FileInfo file, EventProcessor processor, bool verbose, ref int razorEventCount)
{
    var fullName = file.FullName.ToLowerInvariant();

    // Handle .etl.zip — a zipped ETL file (can be very large)
    if (fullName.EndsWith(".etl.zip"))
    {
        LogVerbose(verbose, $"Opening as zipped ETL: {file.Name}");
        ProcessZippedEtl(file, processor, verbose, ref razorEventCount);
    }
    // Handle .nettrace.zip — bundled nettrace files from -- command mode
    else if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
    {
        LogVerbose(verbose, $"Opening as nettrace zip bundle: {file.Name}");
        ProcessNettraceZip(file, processor, verbose, ref razorEventCount);
    }
    else if (file.Extension.Equals(".nettrace", StringComparison.OrdinalIgnoreCase))
    {
        LogVerbose(verbose, $"Opening as EventPipe nettrace: {file.Name}");
        ProcessNettrace(file, processor, verbose, ref razorEventCount);
    }
    else
    {
        LogVerbose(verbose, $"Opening as ETL/ETLX via TraceLog.OpenOrConvert: {file.Name}");
        ProcessEtl(file, processor, verbose, ref razorEventCount);
    }
}

static void ProcessZippedEtl(FileInfo file, EventProcessor processor, bool verbose, ref int razorEventCount)
{
    using var zipArchive = ZipFile.OpenRead(file.FullName);

    // Find .etl entries
    var etlEntries = zipArchive.Entries.Where(e => e.Name.EndsWith(".etl", StringComparison.OrdinalIgnoreCase)).ToList();
    LogVerbose(verbose, $"Zip contains {etlEntries.Count} .etl entry/entries ({zipArchive.Entries.Count} total)");

    if (etlEntries.Count == 0)
    {
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] No .etl files found inside {file.Name}");
        return;
    }

    foreach (var entry in etlEntries)
    {
        AnsiConsole.MarkupLine($"  [dim]Extracting: {entry.Name} ({entry.Length / (1024.0 * 1024.0):F1} MB compressed → {entry.Length / (1024.0 * 1024.0):F1} MB)[/]");
        LogVerbose(verbose, $"Extracting {entry.FullName} ({entry.CompressedLength / 1024.0:F1} KB compressed, {entry.Length / 1024.0:F1} KB uncompressed)");

        // Stream the entry to a temp file — avoids loading the full ETL into memory
        var tempFile = Path.Combine(Path.GetTempPath(), $"generatorlog-{Guid.NewGuid():N}.etl");
        try
        {
            LogVerbose(verbose, $"Streaming to temp file: {tempFile}");
            using (var entryStream = entry.Open())
            using (var tempStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920))
            {
                entryStream.CopyTo(tempStream, bufferSize: 81920);
            }

            LogVerbose(verbose, $"Extraction complete, processing ETL...");
            ProcessEtl(new FileInfo(tempFile), processor, verbose, ref razorEventCount);
        }
        finally
        {
            LogVerbose(verbose, $"Cleaning up temp: {tempFile}");
            try { File.Delete(tempFile); } catch { }
        }
    }
}

static void ProcessNettraceZip(FileInfo file, EventProcessor processor, bool verbose, ref int razorEventCount)
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"generatorlog-{Guid.NewGuid():N}");
    LogVerbose(verbose, $"Extracting zip to temp: {tempDir}");
    Directory.CreateDirectory(tempDir);
    try
    {
        ZipFile.ExtractToDirectory(file.FullName, tempDir);
        var entries = Directory.GetFiles(tempDir, "*.nettrace");
        LogVerbose(verbose, $"Zip contains {entries.Length} .nettrace file(s)");
        foreach (var entry in entries)
        {
            AnsiConsole.MarkupLine($"  [dim]Processing entry: {Path.GetFileName(entry)}[/]");
            ProcessTraceFile(new FileInfo(entry), processor, verbose, ref razorEventCount);
        }
    }
    finally
    {
        LogVerbose(verbose, $"Cleaning up temp: {tempDir}");
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }
}

static void ProcessNettrace(FileInfo file, EventProcessor processor, bool verbose, ref int razorEventCount)
{
    int localRazor = 0;
    using var source = new EventPipeEventSource(file.FullName);
    source.Dynamic.AddCallbackForProviderEvents(
        (providerName, eventName) => providerName is EventProcessor.CodeAnalysisEtwName or "Microsoft-DotNet-SDK-Razor-SourceGenerator"
            ? EventFilterResponse.AcceptEvent
            : EventFilterResponse.RejectProvider,
        (TraceEvent e) =>
        {
            if (e.ProviderName == EventProcessor.CodeAnalysisEtwName)
                processor.ProcessEvent(e);
            else if (e.ProviderName == "Microsoft-DotNet-SDK-Razor-SourceGenerator")
                localRazor++;
        });
    source.Process();
    razorEventCount += localRazor;
    LogVerbose(verbose, "EventPipe processing complete");
}

static void ProcessEtl(FileInfo file, EventProcessor processor, bool verbose, ref int razorEventCount)
{
    using var traceLog = TraceLog.OpenOrConvert(file.FullName);
    LogVerbose(verbose, $"TraceLog opened: {traceLog.Events.Count()} total events");
    int localRazor = 0;
    foreach (var e in traceLog.Events)
    {
        if (e.ProviderName == EventProcessor.CodeAnalysisEtwName)
            processor.ProcessEvent(e);
        else if (e.ProviderName == "Microsoft-DotNet-SDK-Razor-SourceGenerator")
            localRazor++;
    }
    razorEventCount += localRazor;
    LogVerbose(verbose, "ETL processing complete");
}

static void LogVerbose(bool verbose, string message)
{
    if (!verbose) return;
    AnsiConsole.MarkupLine($"[grey]  [verbose] {Markup.Escape(message)}[/]");
}
