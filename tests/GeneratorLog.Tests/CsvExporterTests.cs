using GeneratorLog.Analyze;

namespace GeneratorLog.Tests;

public class CsvExporterTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    [InlineData("", "")]
    public void Escape_HandlesSpecialCharacters(string input, string expected)
    {
        Assert.Equal(expected, CsvExporter.Escape(input));
    }

    [Theory]
    [InlineData(@"C:\Users\foo\.nuget\packages\mygen\1.0\lib\MyGen.dll", "MyGen.dll")]
    [InlineData("MyGen.dll", "MyGen.dll")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void ShortenAssemblyPath_ExtractsFileName(string input, string expected)
    {
        Assert.Equal(expected, CsvExporter.ShortenAssemblyPath(input));
    }

    [Fact]
    public void Export_WithSingleProcess_ProducesCorrectCsv()
    {
        var time = new EventTime(
            new DateTime(2026, 3, 18, 10, 0, 0),
            new DateTime(2026, 3, 18, 10, 0, 0, 150),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1.15),
            TimeSpan.FromMilliseconds(150));

        var processes = new List<ProcessInfo>
        {
            new("dotnet (1234)",
                [new GeneratorInfo("MyGenerator", "MyGen.dll", [new GeneratorRun(0, "MyProject", time, [])])],
                [],
                1)
        };

        var csv = CsvExporter.Export(processes);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal("process,generator_name,generator_assembly,run_id,project,start_time,execution_time_ms", lines[0]);
        Assert.Contains("dotnet (1234)", lines[1]);
        Assert.Contains("MyGenerator", lines[1]);
        Assert.Contains("MyProject", lines[1]);
        Assert.Contains("150.000", lines[1]);
    }

    [Fact]
    public void Export_WithNoData_ProducesHeaderOnly()
    {
        var csv = CsvExporter.Export([]);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines);
        Assert.StartsWith("process,", lines[0]);
    }

    [Fact]
    public void Export_WithCommasInNames_EscapesProperly()
    {
        var time = new EventTime(
            DateTime.Now, DateTime.Now, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        var processes = new List<ProcessInfo>
        {
            new("process,with,commas",
                [new GeneratorInfo("gen,name", "asm.dll", [new GeneratorRun(0, "proj", time, [])])],
                [],
                1)
        };

        var csv = CsvExporter.Export(processes);

        Assert.Contains("\"process,with,commas\"", csv);
        Assert.Contains("\"gen,name\"", csv);
    }
}
