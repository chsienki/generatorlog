namespace GeneratorLog;

/// <summary>
/// Resolves output file paths with collision avoidance.
/// </summary>
public static class OutputPath
{
    public static string Resolve(string? explicitPath, string directory, string baseName = "generators", string extension = ".etl")
    {
        if (explicitPath is not null)
            return Path.GetFullPath(explicitPath);

        var candidate = Path.Combine(directory, baseName + extension);

        if (!File.Exists(candidate))
            return candidate;

        for (int i = 1; ; i++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({i}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
