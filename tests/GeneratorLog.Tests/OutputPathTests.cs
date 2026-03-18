using GeneratorLog;

namespace GeneratorLog.Tests;

public class OutputPathTests : IDisposable
{
    private readonly string _tempDir;

    public OutputPathTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"GeneratorLogTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Resolve_WithExplicitPath_ReturnsFullPath()
    {
        var result = OutputPath.Resolve(@"C:\traces\my.etl", _tempDir);

        Assert.Equal(@"C:\traces\my.etl", result);
    }

    [Fact]
    public void Resolve_WithNoExplicitPath_ReturnsDefaultInDirectory()
    {
        var result = OutputPath.Resolve(null, _tempDir);

        Assert.Equal(Path.Combine(_tempDir, "generators.etl"), result);
    }

    [Fact]
    public void Resolve_WhenDefaultExists_AppendsNumber()
    {
        File.WriteAllText(Path.Combine(_tempDir, "generators.etl"), "");

        var result = OutputPath.Resolve(null, _tempDir);

        Assert.Equal(Path.Combine(_tempDir, "generators (1).etl"), result);
    }

    [Fact]
    public void Resolve_WhenMultipleExist_FindsNextAvailable()
    {
        File.WriteAllText(Path.Combine(_tempDir, "generators.etl"), "");
        File.WriteAllText(Path.Combine(_tempDir, "generators (1).etl"), "");
        File.WriteAllText(Path.Combine(_tempDir, "generators (2).etl"), "");

        var result = OutputPath.Resolve(null, _tempDir);

        Assert.Equal(Path.Combine(_tempDir, "generators (3).etl"), result);
    }

    [Fact]
    public void Resolve_WithGapInNumbers_FillsGap()
    {
        File.WriteAllText(Path.Combine(_tempDir, "generators.etl"), "");
        // Skip (1), create (2)
        File.WriteAllText(Path.Combine(_tempDir, "generators (2).etl"), "");

        var result = OutputPath.Resolve(null, _tempDir);

        Assert.Equal(Path.Combine(_tempDir, "generators (1).etl"), result);
    }

    [Fact]
    public void Resolve_WithCustomBaseName_UsesIt()
    {
        var result = OutputPath.Resolve(null, _tempDir, baseName: "trace", extension: ".log");

        Assert.Equal(Path.Combine(_tempDir, "trace.log"), result);
    }
}
