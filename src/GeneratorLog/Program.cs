using System.Diagnostics;
using System.Diagnostics.Tracing;
using GeneratorLog;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

string? outputPath = null;
int? pid = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--output" or "-o" && i + 1 < args.Length)
    {
        outputPath = args[++i];
    }
    else if (args[i] is "--pid" or "-p" && i + 1 < args.Length)
    {
        if (!int.TryParse(args[++i], out var parsedPid))
        {
            Console.Error.WriteLine($"Error: Invalid PID '{args[i]}'.");
            return 1;
        }
        pid = parsedPid;
    }
    else if (args[i] is "--help" or "-h" or "-?")
    {
        PrintHelp();
        return 0;
    }
}

return await Run(outputPath, pid);

static void PrintHelp()
{
    Console.WriteLine("Usage: generatorlog [options]");
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
        Console.WriteLine("  --pid, -p <pid>      (Required on non-Windows) The process ID to trace.");
    }
}

static async Task<int> Run(string? outputPath, int? pid)
{
    // On non-Windows, EventPipe is the only option and requires a PID
    if (!OperatingSystem.IsWindows() && pid is null)
    {
        Console.Error.WriteLine("On this platform, you must specify the process ID to trace.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  generatorlog --pid <pid>");
        Console.Error.WriteLine();
        Console.Error.WriteLine("To find the PID of a running dotnet build, use:");
        Console.Error.WriteLine("  dotnet build & echo $!          # bash");
        Console.Error.WriteLine("  ps aux | grep dotnet            # find running dotnet processes");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Or wrap your build command:");
        Console.Error.WriteLine("  dotnet build &");
        Console.Error.WriteLine("  generatorlog --pid $!");
        return 1;
    }

    // If a PID is specified (any platform), use EventPipe
    if (pid is not null)
        return await RunEventPipe(outputPath, pid.Value);

    // Otherwise, use ETW (Windows only at this point)
    return await RunEtw(outputPath);
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

    using var session = new TraceEventSession("GeneratorLog-Session", resolvedPath);

    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        session.Stop();
    };

    session.Source.Dynamic.AddCallbackForProviderEvent(
        "Microsoft-CodeAnalysis-General",
        "GeneratorDriverRunTime/Stop",
        (TraceEvent data) => Interlocked.Increment(ref driverRuns));

    session.Source.Dynamic.AddCallbackForProviderEvent(
        "Microsoft-CodeAnalysis-General",
        "SingleGeneratorRunTime/Stop",
        (TraceEvent data) => Interlocked.Increment(ref generatorExecutions));

    session.EnableProvider("Microsoft-CodeAnalysis-General", Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose);

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
