# GeneratorLog

Tools for recording and analyzing Roslyn source generator ETW events.

## Tools

### `generatorlog` — ETW Event Recorder

Record source generator ETW events from the `Microsoft-CodeAnalysis-General` provider to an ETL file.

```
dnx generatorlog [--output|-o <path>]
```

- Requires administrator privileges (auto-elevates on Windows via UAC)
- Default output: `generators.etl` in the current directory (with collision avoidance)
- Shows live progress as generator runs are recorded
- Press `Ctrl+C` to stop recording

### `generatorlog-analyze` — ETL File Analyzer

Analyze ETL files containing Roslyn source generator events and display statistics.

```
dnx generatorlog-analyze [--csv|-c <path>] <file1.etl> [file2.etl ...]
```

- Works with ETL files from `generatorlog` or any trace with `Microsoft-CodeAnalysis-General` events
- Reports per-process and per-generator statistics: execution counts, min/avg/max timing, cumulative time
- Optional CSV export with `--csv`

## Installation

### One-shot via dnx (.NET 10+)

```
dnx generatorlog
dnx generatorlog-analyze trace.etl
```

### As global tools

```
dotnet tool install -g GeneratorLog
dotnet tool install -g GeneratorLog.Analyze
```

## Capturing a trace

1. Run `generatorlog` (as administrator)
2. Build your project with `dotnet build` or in Visual Studio
3. Press `Ctrl+C` to stop recording
4. Analyze with `generatorlog-analyze generators.etl`

## License

MIT
