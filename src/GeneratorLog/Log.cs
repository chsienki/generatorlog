namespace GeneratorLog;

/// <summary>
/// Simple verbose logger that writes grey-coloured messages when enabled.
/// </summary>
public static class Log
{
    public static bool Verbose { get; set; }

    public static void Debug(string message)
    {
        if (!Verbose) return;
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [verbose] {message}");
        Console.ForegroundColor = prev;
    }
}
