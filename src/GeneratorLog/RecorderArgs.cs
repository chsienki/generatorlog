namespace GeneratorLog;

/// <summary>
/// Parsed command-line arguments for the generatorlog recorder.
/// </summary>
public record RecorderOptions(
    string? OutputPath,
    int? Pid,
    string[]? WrappedCommand,
    bool ShowHelp,
    bool Verbose);

/// <summary>
/// Parses command-line arguments for the generatorlog recorder.
/// </summary>
public static class RecorderArgs
{
    public static RecorderOptions Parse(string[] args)
    {
        string? outputPath = null;
        int? pid = null;
        string[]? wrappedCommand = null;
        bool showHelp = false;
        bool verbose = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--")
            {
                wrappedCommand = args[(i + 1)..];
                break;
            }
            else if (args[i] is "--output" or "-o" && i + 1 < args.Length)
            {
                outputPath = args[++i];
            }
            else if (args[i] is "--pid" or "-p" && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var parsedPid))
                    pid = parsedPid;
            }
            else if (args[i] is "--verbose" or "-v")
            {
                verbose = true;
            }
            else if (args[i] is "--help" or "-h" or "-?")
            {
                showHelp = true;
            }
        }

        return new RecorderOptions(outputPath, pid, wrappedCommand, showHelp, verbose);
    }
}
