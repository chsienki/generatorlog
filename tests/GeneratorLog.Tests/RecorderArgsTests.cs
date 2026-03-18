using GeneratorLog;

namespace GeneratorLog.Tests;

public class RecorderArgsTests
{
    [Fact]
    public void Parse_NoArgs_ReturnsDefaults()
    {
        var options = RecorderArgs.Parse([]);

        Assert.Null(options.OutputPath);
        Assert.Null(options.Pid);
        Assert.Null(options.WrappedCommand);
        Assert.False(options.ShowHelp);
        Assert.False(options.Verbose);
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("-v")]
    public void Parse_VerboseFlag_SetsVerbose(string flag)
    {
        var options = RecorderArgs.Parse([flag]);

        Assert.True(options.Verbose);
    }

    [Fact]
    public void Parse_VerboseWithOtherOptions_SetsBoth()
    {
        var options = RecorderArgs.Parse(["-v", "--output", "trace.etl"]);

        Assert.True(options.Verbose);
        Assert.Equal("trace.etl", options.OutputPath);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Parse_HelpFlag_SetsShowHelp(string flag)
    {
        var options = RecorderArgs.Parse([flag]);

        Assert.True(options.ShowHelp);
    }

    [Theory]
    [InlineData("--output", "trace.etl")]
    [InlineData("-o", "trace.etl")]
    public void Parse_OutputOption_SetsOutputPath(string flag, string path)
    {
        var options = RecorderArgs.Parse([flag, path]);

        Assert.Equal(path, options.OutputPath);
    }

    [Theory]
    [InlineData("--pid", "1234")]
    [InlineData("-p", "1234")]
    public void Parse_PidOption_SetsPid(string flag, string pidStr)
    {
        var options = RecorderArgs.Parse([flag, pidStr]);

        Assert.Equal(1234, options.Pid);
    }

    [Fact]
    public void Parse_InvalidPid_ReturnsNullPid()
    {
        var options = RecorderArgs.Parse(["--pid", "notanumber"]);

        Assert.Null(options.Pid);
    }

    [Fact]
    public void Parse_DoubleDash_CapturesWrappedCommand()
    {
        var options = RecorderArgs.Parse(["--", "dotnet", "build"]);

        Assert.NotNull(options.WrappedCommand);
        Assert.Equal(["dotnet", "build"], options.WrappedCommand!);
    }

    [Fact]
    public void Parse_DoubleDash_WithNoCommand_ReturnsEmptyArray()
    {
        var options = RecorderArgs.Parse(["--"]);

        Assert.NotNull(options.WrappedCommand);
        Assert.Empty(options.WrappedCommand!);
    }

    [Fact]
    public void Parse_OutputBeforeDoubleDash_SetsBoth()
    {
        var options = RecorderArgs.Parse(["-o", "my.nettrace", "--", "dotnet", "build"]);

        Assert.Equal("my.nettrace", options.OutputPath);
        Assert.Equal(["dotnet", "build"], options.WrappedCommand!);
    }

    [Fact]
    public void Parse_OptionsAfterDoubleDash_ArePartOfCommand()
    {
        var options = RecorderArgs.Parse(["--", "dotnet", "build", "--configuration", "Release"]);

        Assert.Null(options.OutputPath);
        Assert.Equal(["dotnet", "build", "--configuration", "Release"], options.WrappedCommand!);
    }

    [Fact]
    public void Parse_PidAndOutput_SetsBoth()
    {
        var options = RecorderArgs.Parse(["--pid", "5678", "--output", "trace.nettrace"]);

        Assert.Equal(5678, options.Pid);
        Assert.Equal("trace.nettrace", options.OutputPath);
    }

    [Fact]
    public void Parse_OutputAtEnd_WithNoValue_DoesNotSet()
    {
        // --output with no following value (at end of args)
        var options = RecorderArgs.Parse(["--output"]);

        Assert.Null(options.OutputPath);
    }

    [Fact]
    public void Parse_PidAtEnd_WithNoValue_DoesNotSet()
    {
        var options = RecorderArgs.Parse(["--pid"]);

        Assert.Null(options.Pid);
    }
}
