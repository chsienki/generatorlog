# GeneratorLog

Tools for recording and analyzing Roslyn source generator events.

## Tools

### `generatorlog` — Trace Recorder

Record source generator events from the `Microsoft-CodeAnalysis-General` provider to a trace file.

**Wrap a build command (any platform):**

```
dnx generatorlog -- dotnet build
```

**Windows (system-wide via ETW, no PID needed):**

```
dnx generatorlog [--output|-o <path>]
```

**Attach to a running process (any platform):**

```
dnx generatorlog --pid <pid> [--output|-o <path>]
```

- `-- <command>`: Launches the command and traces it via EventPipe. Works on all platforms.
- No arguments (Windows only): system-wide ETW capture, auto-elevates via UAC, produces `.etl` files
- `--pid`: attaches to a running process via EventPipe, produces `.nettrace` files
- Default output: `generators.etl` (ETW) or `generators.nettrace` (EventPipe) with collision avoidance
- Shows live progress; press `Ctrl+C` to stop recording

### `generatorlog-analyze` — Trace Analyzer

Analyze trace files containing Roslyn source generator events and display statistics.

```
dnx generatorlog-analyze [--csv|-c <path>] <file.etl|file.nettrace> [...]
```

- Works with `.etl` files (from ETW) and `.nettrace` files (from EventPipe)
- Reports per-process and per-generator statistics: execution counts, min/avg/max timing, cumulative time
- Optional CSV export with `--csv`

## Installation

### One-shot via dnx (.NET 10+)

```
dnx generatorlog -- dotnet build
dnx generatorlog-analyze generators.nettrace
```

### As global tools

```
dotnet tool install -g GeneratorLog --version 0.0.2-alpha
dotnet tool install -g GeneratorLog.Analyze --version 0.0.2-alpha
```

## Customer guide

See [docs/recording-guide.md](docs/recording-guide.md) for step-by-step instructions to give to customers.

## License

MIT
