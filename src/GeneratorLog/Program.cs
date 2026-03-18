using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO.Compression;
using GeneratorLog;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

var options = RecorderArgs.Parse(args);
Log.Verbose = options.Verbose;

if (options.ShowHelp)
{
    PrintHelp();
    return 0;
}

Log.Debug($"Parsed options: Output={options.OutputPath}, PID={options.Pid}, Wrapped={options.WrappedCommand is not null}, Verbose={options.Verbose}");
return await Run(options.OutputPath, options.Pid, options.WrappedCommand);

static void PrintHelp()
{
    Console.WriteLine("Usage: generatorlog [options] [-- <command> [args...]]");
    Console.WriteLine();
    Console.WriteLine("Record Roslyn source generator events to a trace file.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --output, -o <path>  Path to the output trace file.");
    if (OperatingSystem.IsWindows())
    {
        Console.WriteLine("                       Defaults to generators.etl in the current directory.");
        Console.WriteLine("  --pid, -p <pid>      Trace a specific process via EventPipe instead of");
        Console.WriteLine("                       system-wide ETW. Produces a .nettrace file.");
    }
    else
    {
        Console.WriteLine("                       Defaults to generators.nettrace in the current directory.");
        Console.WriteLine("  --pid, -p <pid>      Trace a running process by PID.");
    }
    Console.WriteLine("  -- <command>         Launch a command and trace it. The command is run as a");
    Console.WriteLine("                       child process and traced via EventPipe.");
    Console.WriteLine("  --verbose, -v        Show detailed diagnostic output.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    if (OperatingSystem.IsWindows())
    {
        Console.WriteLine("  generatorlog                              # ETW system-wide (Windows, admin)");
    }
    Console.WriteLine("  generatorlog -- dotnet build               # Launch and trace a build");
    Console.WriteLine("  generatorlog --pid 12345                   # Attach to a running process");
}

static async Task<int> Run(string? outputPath, int? pid, string[]? wrappedCommand)
{
    // -- <command> mode: launch and trace
    if (wrappedCommand is { Length: > 0 })
        return await RunWrapped(outputPath, wrappedCommand);

    // --pid mode: attach to existing process
    if (pid is not null)
        return await RunEventPipe(outputPath, pid.Value);

    // On non-Windows without --pid or --, explain options
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("On this platform, specify a command to trace or a process ID:");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  generatorlog -- dotnet build       # Launch and trace a build");
        Console.Error.WriteLine("  generatorlog --pid <pid>           # Attach to a running process");
        return 1;
    }

    // Default: ETW system-wide (Windows only)
    return await RunEtw(outputPath);
}

static async Task<int> RunWrapped(string? outputPath, string[] command)
{
    var resolvedPath = OutputPath.Resolve(outputPath, Directory.GetCurrentDirectory(),
        baseName: "generators", extension: ".nettrace");
    Log.Debug($"Output path resolved to: {resolvedPath}");

    var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var psi = new ProcessStartInfo
    {
        FileName = command[0],
        UseShellExecute = false,
    };
    foreach (var arg in command.Skip(1))
        psi.ArgumentList.Add(arg);

    // Enable environment-based EventPipe tracing for the entire process tree
    psi.Environment["DOTNET_EnableEventPipe"] = "1";
    psi.Environment["DOTNET_EventPipeConfig"] = "Microsoft-CodeAnalysis-General:0xFFFFFFFFFFFFFFFF:5";
    psi.Environment["DOTNET_MSBUILD_DISABLENODEREUSE"] = "1";
    psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
    psi.Environment["UseSharedCompilation"] = "false";

    Log.Debug("Environment variables set on child process:");
    Log.Debug("  DOTNET_EnableEventPipe=1");
    Log.Debug("  DOTNET_EventPipeConfig=Microsoft-CodeAnalysis-General:0xFFFFFFFFFFFFFFFF:5");
    Log.Debug("  DOTNET_MSBUILD_DISABLENODEREUSE=1");
    Log.Debug("  MSBUILDDISABLENODEREUSE=1");
    Log.Debug("  UseSharedCompilation=false");

    Console.WriteLine($"Launching: {string.Join(" ", command)}");
    Console.WriteLine($"Recording generator events to: {resolvedPath}");
    Console.WriteLine();

    var startTime = DateTime.Now;
    Log.Debug($"Recording start time: {startTime:O}");

    Process childProcess;
    try
    {
        childProcess = Process.Start(psi)!;
        Log.Debug($"Child process started: PID {childProcess.Id}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: Failed to start '{command[0]}': {ex.Message}");
        return 1;
    }

    _ = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.Write($"\rRecording from PID {childProcess.Id}...   ");
            try { await Task.Delay(500, cts.Token); } catch (OperationCanceledException) { break; }
        }
    });

    try
    {
        await childProcess.WaitForExitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        try { childProcess.Kill(entireProcessTree: true); } catch { }
    }

    await cts.CancelAsync();
    Log.Debug($"Child process exited with code {childProcess.ExitCode}");

    Console.WriteLine();
    Console.Write("Collecting trace files...");

    var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Directory.GetCurrentDirectory(),
        Path.GetDirectoryName(resolvedPath)!
    };
    foreach (var arg in command.Skip(1))
    {
        if (Directory.Exists(arg))
            searchDirs.Add(Path.GetFullPath(arg));
        else if (File.Exists(arg))
            searchDirs.Add(Path.GetDirectoryName(Path.GetFullPath(arg))!);
    }

    Log.Debug($"Searching directories: {string.Join(", ", searchDirs)}");
    Log.Debug($"Looking for .nettrace files modified after {startTime:O}");

    var traceFiles = searchDirs
        .Where(Directory.Exists)
        .SelectMany(dir =>
        {
            try { return Directory.GetFiles(dir, "*.nettrace", SearchOption.TopDirectoryOnly); }
            catch { return []; }
        })
        .Where(f => new FileInfo(f).LastWriteTime >= startTime && new FileInfo(f).Length > 1024)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Console.WriteLine($" found {traceFiles.Length} file(s).");
    foreach (var f in traceFiles)
        Log.Debug($"  Found: {f} ({new FileInfo(f).Length / 1024.0:F1} KB, modified {new FileInfo(f).LastWriteTime:O})");

    if (traceFiles.Length == 0)
    {
        Console.WriteLine();
        Console.WriteLine("Recording complete. No trace files were produced.");
        Console.WriteLine("The build may not have used any source generators.");
    }
    else
    {
        // Filter to files that actually contain CodeAnalysis events
        Console.Write("Scanning for generator events...");
        var filesWithEvents = new List<string>();
        foreach (var f in traceFiles)
        {
            Log.Debug($"Scanning {f} for CodeAnalysis events...");
            if (HasCodeAnalysisEvents(f))
            {
                Log.Debug($"  → contains generator events");
                filesWithEvents.Add(f);
            }
            else
            {
                Log.Debug($"  → no generator events");
            }
        }
        Console.WriteLine($" {filesWithEvents.Count} file(s) with events.");

        if (filesWithEvents.Count == 0)
        {
            var largest = traceFiles.OrderByDescending(f => new FileInfo(f).Length).First();
            Log.Debug($"No files with events. Keeping largest: {largest}");
            if (!string.Equals(largest, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug($"Moving {largest} → {resolvedPath}");
                File.Move(largest, resolvedPath, overwrite: true);
            }

            Console.WriteLine();
            Console.WriteLine($"Recording complete. Saved to: {resolvedPath}");
            Console.WriteLine("Note: No generator events were found in the trace.");
        }
        else if (filesWithEvents.Count == 1)
        {
            if (!string.Equals(filesWithEvents[0], resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug($"Moving {filesWithEvents[0]} → {resolvedPath}");
                File.Move(filesWithEvents[0], resolvedPath, overwrite: true);
            }

            Console.WriteLine();
            Console.WriteLine($"Recording complete. Saved to: {resolvedPath}");
        }
        else
        {
            var zipPath = Path.ChangeExtension(resolvedPath, ".nettrace.zip");
            Console.Write($"Merging {filesWithEvents.Count} trace files into {Path.GetFileName(zipPath)}...");
            Log.Debug($"Creating zip archive: {zipPath}");

            using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                for (int i = 0; i < filesWithEvents.Count; i++)
                {
                    var entryName = $"trace-{i + 1}.nettrace";
                    Log.Debug($"Adding {filesWithEvents[i]} as {entryName} ({new FileInfo(filesWithEvents[i]).Length / 1024.0:F1} KB)");
                    zip.CreateEntryFromFile(filesWithEvents[i], entryName, System.IO.Compression.CompressionLevel.Optimal);
                }
            }

            Console.WriteLine(" done.");
            Console.WriteLine();
            Console.WriteLine($"Recording complete. Saved to: {zipPath}");
        }

        // Clean up all collected trace files
        foreach (var f in traceFiles)
        {
            Log.Debug($"Deleting: {f}");
            try { File.Delete(f); } catch { }
        }
    }

    Console.WriteLine("Use generatorlog-analyze to view the results.");
    return childProcess.ExitCode;
}

static async Task<int> RunEtw(string? outputPath)
{
    // Check for admin elevation
    if (!(TraceEventSession.IsElevated() ?? false))
    {
        Console.Error.WriteLine("ETW event tracing requires administrator privileges.");

        if (OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Attempting to re-launch as administrator...");
            try
            {
                var exe = Environment.ProcessPath!;
                var arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var process = Process.Start(psi);
                if (process is not null)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to elevate: {ex.Message}");
            }
        }

        Console.Error.WriteLine("Please run from an elevated (Administrator) command prompt.");
        return 1;
    }

    var resolvedPath = OutputPath.Resolve(outputPath, Directory.GetCurrentDirectory());
    Log.Debug($"Output path resolved to: {resolvedPath}");
    Console.WriteLine($"Recording generator events (ETW, system-wide) to: {resolvedPath}");
    Console.WriteLine("Press Ctrl+C to stop recording.");
    Console.WriteLine();

    int driverRuns = 0;
    int generatorExecutions = 0;
    var cts = new CancellationTokenSource();

    Log.Debug("Creating real-time ETW session: GeneratorLog-Session");
    using var session = new TraceEventSession("GeneratorLog-Session");
    session.EnableProvider("Microsoft-CodeAnalysis-General", Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose);
    Log.Debug("Enabled provider Microsoft-CodeAnalysis-General (Verbose) on real-time session");

    Log.Debug($"Creating file ETW session: GeneratorLog-FileSession → {resolvedPath}");
    using var fileSession = new TraceEventSession("GeneratorLog-FileSession", resolvedPath);
    fileSession.EnableProvider("Microsoft-CodeAnalysis-General", Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose);
    Log.Debug("Enabled provider Microsoft-CodeAnalysis-General (Verbose) on file session");

    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        session.Stop();
        fileSession.Stop();
    };

    session.Source.Dynamic.AddCallbackForProviderEvent(
        "Microsoft-CodeAnalysis-General",
        "GeneratorDriverRunTime/Stop",
        (TraceEvent data) => Interlocked.Increment(ref driverRuns));

    session.Source.Dynamic.AddCallbackForProviderEvent(
        "Microsoft-CodeAnalysis-General",
        "SingleGeneratorRunTime/Stop",
        (TraceEvent data) => Interlocked.Increment(ref generatorExecutions));

    _ = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.Write($"\rRecorded {driverRuns} driver run(s), {generatorExecutions} generator execution(s)   ");
            try { await Task.Delay(500, cts.Token); } catch (OperationCanceledException) { break; }
        }
    });

    await Task.Run(() => session.Source.Process());

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine($"Recording complete. {driverRuns} driver run(s), {generatorExecutions} generator execution(s).");
    Console.WriteLine($"Saved to: {resolvedPath}");
    return 0;
}

static async Task<int> RunEventPipe(string? outputPath, int pid)
{
    try
    {
        using var proc = Process.GetProcessById(pid);
        Console.WriteLine($"Attaching to process: {proc.ProcessName} (PID {pid})");
        Log.Debug($"Target process: {proc.ProcessName} (PID {pid})");
    }
    catch (ArgumentException)
    {
        Console.Error.WriteLine($"Error: No process found with PID {pid}.");
        return 1;
    }

    var resolvedPath = OutputPath.Resolve(outputPath, Directory.GetCurrentDirectory(),
        baseName: "generators", extension: ".nettrace");
    Log.Debug($"Output path resolved to: {resolvedPath}");
    Console.WriteLine($"Recording generator events (EventPipe) to: {resolvedPath}");
    Console.WriteLine("Press Ctrl+C to stop recording.");
    Console.WriteLine();

    var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var provider = new EventPipeProvider(
        "Microsoft-CodeAnalysis-General",
        EventLevel.Verbose);
    Log.Debug("Configured EventPipe provider: Microsoft-CodeAnalysis-General (Verbose)");

    var client = new DiagnosticsClient(pid);
    EventPipeSession? session = null;

    Log.Debug($"Connecting to diagnostic port for PID {pid}...");
    try
    {
        session = client.StartEventPipeSession([provider], requestRundown: false);
        Log.Debug("EventPipe session started successfully");
    }
    catch (ServerNotAvailableException)
    {
        Console.Error.WriteLine($"Error: Could not connect to process {pid}.");
        Console.Error.WriteLine("Ensure the target process is a .NET application and is still running.");
        return 1;
    }

    Log.Debug($"Writing EventPipe stream to: {resolvedPath}");
    var writeTask = Task.Run(async () =>
    {
        using var fs = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write);
        try
        {
            await session.EventStream.CopyToAsync(fs, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (cts.IsCancellationRequested) { }
    });

    _ = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.Write($"\rRecording from PID {pid}... (Ctrl+C to stop)   ");
            try { await Task.Delay(500, cts.Token); } catch (OperationCanceledException) { break; }
        }
    });

    // Wait for either cancellation or the target process to exit
    _ = Task.Run(async () =>
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            await proc.WaitForExitAsync(cts.Token);
            // Process exited, stop recording
            await cts.CancelAsync();
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException)
        {
            // Process already gone
            await cts.CancelAsync();
        }
    });

    try
    {
        await writeTask;
    }
    catch (Exception) when (cts.IsCancellationRequested) { }
    finally
    {
        try { session.Stop(); } catch { }
        session.Dispose();
    }

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine($"Recording complete. Saved to: {resolvedPath}");
    Console.WriteLine("Use generatorlog-analyze to view the results.");
    return 0;
}

static bool HasCodeAnalysisEvents(string nettraceFile)
{
    try
    {
        bool found = false;
        using var source = new EventPipeEventSource(nettraceFile);
        source.Dynamic.AddCallbackForProviderEvents(
            (providerName, _) => providerName == "Microsoft-CodeAnalysis-General"
                ? EventFilterResponse.AcceptEvent
                : EventFilterResponse.RejectProvider,
            (TraceEvent _) => { found = true; source.StopProcessing(); });
        source.Process();
        return found;
    }
    catch
    {
        return false;
    }
}
