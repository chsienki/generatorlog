using System.Text;

namespace GeneratorLog.Analyze;

/// <summary>
/// Exports generator execution data to CSV format.
/// </summary>
public static class CsvExporter
{
    public static string Export(IReadOnlyList<ProcessInfo> processes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("process,generator_name,generator_assembly,run_id,project,start_time,execution_time_ms");

        foreach (var process in processes)
        {
            foreach (var generator in process.Generators)
            {
                foreach (var execution in generator.Executions)
                {
                    sb.AppendLine(string.Join(",",
                        Escape(process.Name),
                        Escape(generator.Name),
                        Escape(generator.Assembly),
                        execution.DriverRun,
                        Escape(execution.ProjectName),
                        execution.Time.Start.ToString("HH:mm:ss.fff"),
                        execution.Time.Duration.TotalMilliseconds.ToString("F3")));
                }
            }
        }

        return sb.ToString();
    }

    public static void ExportToFile(IReadOnlyList<ProcessInfo> processes, string path)
        => File.WriteAllText(path, Export(processes));

    public static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    public static string ShortenAssemblyPath(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return "";

        var fileName = Path.GetFileName(assemblyPath);
        return string.IsNullOrEmpty(fileName) ? assemblyPath : fileName;
    }
}
