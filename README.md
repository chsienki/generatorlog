# GeneratorLog

Tools for recording and analyzing Roslyn source generator ETW events.

## Tools

### `generatorlog` — Trace Recorder

Record source generator events from the `Microsoft-CodeAnalysis-General` provider to a trace file.

**Windows (system-wide via ETW, no PID needed):**

```
dnx generatorlog [--output|-o <path>]
```

**macOS / Linux (per-process via EventPipe):**

```
dnx generatorlog --pid <pid> [--output|-o <path>]
```

- On Windows: auto-elevates via UAC for system-wide ETW capture, produces `.etl` files
- On macOS/Linux: traces a specific process via EventPipe, produces `.nettrace` files
- On Windows you can also use `--pid` to trace a specific process via EventPipe
- Default output: `generators.etl` or `generators.nettrace` (with collision avoidance)
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
dnx generatorlog
dnx generatorlog-analyze trace.etl
```

### As global tools

```
dotnet tool install -g GeneratorLog
dotnet tool install -g GeneratorLog.Analyze
```

## Customer guide

See [docs/recording-guide.md](docs/recording-guide.md) for step-by-step instructions to give to customers.

## License

MIT
