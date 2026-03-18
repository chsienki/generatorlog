using System.Diagnostics;
using GeneratorLog;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--output" or "-o" && i + 1 < args.Length)
    {
        outputPath = args[++i];
    }
    else if (args[i] is "--help" or "-h" or "-?")
    {
        Console.WriteLine("Usage: generatorlog [--output|-o <path>]");
        Console.WriteLine();
        Console.WriteLine("Record Roslyn source generator ETW events to an ETL file.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --output, -o <path>  Path to the output ETL file.");
        Console.WriteLine("                       Defaults to generators.etl in the current directory.");
        return 0;
    }
}

return await Run(outputPath is not null ? new FileInfo(outputPath) : null);

static async Task<int> Run(FileInfo? outputFile)
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

    var outputPath = OutputPath.Resolve(
        outputFile?.FullName,
        Directory.GetCurrentDirectory());
    Console.WriteLine($"Recording generator events to: {outputPath}");
    Console.WriteLine("Press Ctrl+C to stop recording.");
    Console.WriteLine();

    int driverRuns = 0;
    int generatorExecutions = 0;
    var cts = new CancellationTokenSource();

    using var session = new TraceEventSession("GeneratorLog-Session", outputPath);

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

    session.EnableProvider("Microsoft-CodeAnalysis-General", TraceEventLevel.Verbose);

    // Background task to update the console
    _ = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            var d = driverRuns;
            var g = generatorExecutions;
            Console.Write($"\rRecorded {d} driver run(s), {g} generator execution(s)   ");
            try { await Task.Delay(500, cts.Token); } catch (OperationCanceledException) { break; }
        }
    });

    // Process blocks until the session is stopped
    await Task.Run(() => session.Source.Process());

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine($"Recording complete. {driverRuns} driver run(s), {generatorExecutions} generator execution(s).");
    Console.WriteLine($"Saved to: {outputPath}");
    return 0;
}
