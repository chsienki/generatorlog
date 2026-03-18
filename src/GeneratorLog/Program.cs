using System.Diagnostics;
using System.Diagnostics.Tracing;
using GeneratorLog;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

var options = RecorderArgs.Parse(args);

if (options.ShowHelp)
{
    PrintHelp();
    return 0;
}

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

    // Launch the child process in a suspended-like state using DiagnosticsClient
    // We use dotnet's diagnostic port to attach before the process starts work
    var psi = new ProcessStartInfo
    {
        FileName = command[0],
        UseShellExecute = false,
    };
    foreach (var arg in command.Skip(1))
        psi.ArgumentList.Add(arg);

    Console.WriteLine($"Launching: {string.Join(" ", command)}");

    Process childProcess;
    try
    {
        childProcess = Process.Start(psi)!;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: Failed to start '{command[0]}': {ex.Message}");
        return 1;
    }

    Console.WriteLine($"Recording generator events (EventPipe, PID {childProcess.Id}) to: {resolvedPath}");
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

    var client = new DiagnosticsClient(childProcess.Id);
    EventPipeSession? session = null;

    // Retry briefly — the diagnostic server may not be ready immediately
    for (int attempt = 0; attempt < 10; attempt++)
    {
        try
        {
            session = client.StartEventPipeSession([provider], requestRundown: false);
            break;
        }
        catch (ServerNotAvailableException)
        {
            if (childProcess.HasExited)
            {
                Console.Error.WriteLine("Error: Process exited before tracing could start.");
                Console.Error.WriteLine("Ensure the command runs a .NET application.");
                return 1;
            }
            await Task.Delay(100);
        }
    }

    if (session is null)
    {
        Console.Error.WriteLine($"Error: Could not connect to process {childProcess.Id}.");
        Console.Error.WriteLine("Ensure the command runs a .NET application.");
        return 1;
    }

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
            Console.Write($"\rRecording from PID {childProcess.Id}...   ");
            try { await Task.Delay(500, cts.Token); } catch (OperationCanceledException) { break; }
        }
    });

    // Wait for the child process to exit
    try
    {
        await childProcess.WaitForExitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        // User pressed Ctrl+C — kill the child
        try { childProcess.Kill(entireProcessTree: true); } catch { }
    }

    // Give a moment for remaining events to flush
    try { await Task.Delay(500, cts.Token); } catch (OperationCanceledException) { }
    await cts.CancelAsync();

    try { await writeTask; } catch { }
    try { session.Stop(); } catch { }
    session.Dispose();

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine($"Recording complete. Saved to: {resolvedPath}");
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
    Console.WriteLine($"Recording generator events (ETW, system-wide) to: {resolvedPath}");
    Console.WriteLine("Press Ctrl+C to stop recording.");
    Console.WriteLine();

    int driverRuns = 0;
    int generatorExecutions = 0;
    var cts = new CancellationTokenSource();

    // Create a real-time session for live event callbacks, and set a file to capture the ETL
    using var session = new TraceEventSession("GeneratorLog-Session");
    session.EnableProvider("Microsoft-CodeAnalysis-General", Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose);

    // Start a secondary file-logging session that captures the same provider to an ETL file
    using var fileSession = new TraceEventSession("GeneratorLog-FileSession", resolvedPath);
    fileSession.EnableProvider("Microsoft-CodeAnalysis-General", Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose);

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
    // Verify the process exists
    try
    {
        using var proc = Process.GetProcessById(pid);
        Console.WriteLine($"Attaching to process: {proc.ProcessName} (PID {pid})");
    }
    catch (ArgumentException)
    {
        Console.Error.WriteLine($"Error: No process found with PID {pid}.");
        return 1;
    }

    var resolvedPath = OutputPath.Resolve(outputPath, Directory.GetCurrentDirectory(),
        baseName: "generators", extension: ".nettrace");
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

    var client = new DiagnosticsClient(pid);
    EventPipeSession? session = null;

    try
    {
        session = client.StartEventPipeSession([provider], requestRundown: false);
    }
    catch (ServerNotAvailableException)
    {
        Console.Error.WriteLine($"Error: Could not connect to process {pid}.");
        Console.Error.WriteLine("Ensure the target process is a .NET application and is still running.");
        return 1;
    }

    // Copy the stream to file, and count events via a separate read
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
