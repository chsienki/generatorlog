# GeneratorLog

Tools for recording and analyzing Roslyn source generator events.

## Tools

### `generatorlog` — Trace Recorder

Record source generator events from the `Microsoft-CodeAnalysis-General` provider to a trace file.

**Wrap a build command (recommended, any platform):**

```
dnx generatorlog@0.0.3-alpha -- dotnet build
```

**Windows (system-wide via ETW, no PID needed):**

```
dnx generatorlog@0.0.3-alpha
```

**Attach to a running process (any platform):**

```
dnx generatorlog@0.0.3-alpha --pid <pid>
```

**Options:**
| Option | Description |
|---|---|
| `-- <command>` | Launch a command and trace it via EventPipe. Works on all platforms. |
| `--output, -o <path>` | Output file path. Defaults to `generators.etl` (ETW) or `generators.nettrace` (EventPipe). |
| `--pid, -p <pid>` | Attach to a running process via EventPipe. |
| `--verbose, -v` | Show detailed diagnostic output (file operations, ETW/EventPipe setup, etc). |

**Notes:**
- `-- <command>` mode automatically handles MSBuild's multi-process architecture: disables node reuse and compiler server, collects trace files from all child processes, and produces a single output file.
- If multiple processes emit generator events (multi-project builds), they are bundled into a `.nettrace.zip` archive.
- Default output filenames use collision avoidance: `generators.etl`, `generators (1).etl`, etc.

### `generatorlog-analyze` — Trace Analyzer

Analyze trace files containing Roslyn source generator events and display statistics.

```
dnx generatorlog-analyze@0.0.3-alpha [options] <file.etl|file.nettrace|file.zip> [...]
```

**Options:**
| Option | Description |
|---|---|
| `--csv, -c <path>` | Export results as CSV. |
| `--verbose, -v` | Show detailed diagnostic output. |

**Supported formats:** `.etl` (ETW), `.nettrace` (EventPipe), `.nettrace.zip` (bundled traces from `-- <command>` mode).

**Output includes:**
- Summary: total driver runs, generator executions, unique generators, process count
- Per-process overview table
- Per-generator statistics: execution count, min/avg/max timing, cumulative time, projects

## Installation

### One-shot via dnx (.NET 10+)

```
dnx generatorlog@0.0.3-alpha -- dotnet build
dnx generatorlog-analyze@0.0.3-alpha generators.nettrace
```

### As global tools

```
dotnet tool install -g GeneratorLog --version 0.0.3-alpha
dotnet tool install -g GeneratorLog.Analyze --version 0.0.3-alpha
```

## Customer guide

See [docs/recording-guide.md](docs/recording-guide.md) for step-by-step instructions to give to customers.

## License

MIT
