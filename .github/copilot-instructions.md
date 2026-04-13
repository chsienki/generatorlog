# Copilot Instructions for GeneratorLog

## Project Overview

GeneratorLog is a pair of .NET 10 dotnet tools for recording and analyzing Roslyn source generator ETW events:

- **GeneratorLog** (`src/GeneratorLog/`) — Records `Microsoft-CodeAnalysis-General` and `Microsoft-DotNet-SDK-Razor-SourceGenerator` ETW/EventPipe events to trace files
- **GeneratorLog.Analyze** (`src/GeneratorLog.Analyze/`) — Reads trace files and displays per-generator timing statistics

Both tools are packaged as NuGet dotnet tools (via `PackAsTool=true`) and are designed to be run one-shot with `dnx` (.NET 10's tool execution script).

## Repository Structure

```
GeneratorLog.slnx              # Solution file
Directory.Build.props           # Shared build properties (TFM, version, NuGet metadata)
publish.ps1                     # Build, test, pack, and push to NuGet
src/
  GeneratorLog/                 # Recorder tool
    Program.cs                  # Entry point with 3 modes: ETW, EventPipe (--pid), wrapped (-- <cmd>)
    RecorderArgs.cs             # CLI argument parser
    OutputPath.cs               # File path resolution with collision avoidance
    Log.cs                      # Verbose logging helper (grey console output)
  GeneratorLog.Analyze/         # Analyzer tool
    Program.cs                  # Entry point, Spectre.Console rendering
    EventProcessor.cs           # Core event processing logic (adapted from GeneratorETWViewer)
    Models.cs                   # ProcessInfo, GeneratorInfo, GeneratorRun, Transform, etc.
    GeneratorEventData.cs       # Abstraction over TraceEvent for testability
    CsvExporter.cs              # CSV export logic
tests/
  GeneratorLog.Tests/           # xUnit tests
docs/
  recording-guide.md            # Customer-facing step-by-step instructions
```

## Key Technical Details

### ETW Events

The tools capture events from two providers:

#### Microsoft-CodeAnalysis-General (primary)

| ETW Name | EventPipe Name | Purpose |
|---|---|---|
| `GeneratorDriverRunTime/Start` | `StartGeneratorDriverRunTime/Start` | Start of driver run |
| `GeneratorDriverRunTime/Stop` | `StopGeneratorDriverRunTime/Stop` | End of driver run |
| `SingleGeneratorRunTime/Start` | `StartSingleGeneratorRunTime/Start` | Start of individual generator |
| `SingleGeneratorRunTime/Stop` | `StopSingleGeneratorRunTime/Stop` | End of individual generator |
| `BuildStateTable` | `NodeTransform` | Pipeline node transform/caching |

**Important:** Event names differ between ETW (.etl) and EventPipe (.nettrace). The `EventProcessor` handles both naming conventions.

#### Microsoft-DotNet-SDK-Razor-SourceGenerator (supplementary)

The Razor source generator (`Microsoft.NET.Sdk.Razor.SourceGenerators`) has its own EventSource with detailed events like `RazorCodeGenerateStart/Stop`, `ParseRazorDocumentStart/Stop`, `DiscoverTagHelpersFromCompilationStart/Stop`, etc. These are captured at Informational level.

The analyzer does **not** parse Razor events but counts them and notes their presence in the output:
```
Note: This trace also contains 42 Razor source generator event(s)
(Microsoft-DotNet-SDK-Razor-SourceGenerator).
```

The Razor EventSource definition lives in the `razor` repo at `src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/SourceGenerators/RazorSourceGeneratorEventSource.cs`.

### Recording Modes

1. **ETW (Windows only, admin required):** Uses two `TraceEventSession`s — one real-time for live counting, one file-based for ETL output. Cannot use `.Source` on a file-based session.

2. **EventPipe `--pid`:** Attaches `DiagnosticsClient` to a running process, streams to `.nettrace` via `EventPipeSession.EventStream.CopyToAsync`.

3. **EventPipe `-- <command>` (recommended cross-platform):** Sets environment variables on child process:
   - `DOTNET_EnableEventPipe=1` — enables EventPipe for all .NET processes in the tree
   - `DOTNET_EventPipeConfig=Microsoft-CodeAnalysis-General:0xFFFFFFFFFFFFFFFF:5,Microsoft-DotNet-SDK-Razor-SourceGenerator:0xFFFFFFFFFFFFFFFF:4` — both providers
   - `DOTNET_MSBUILD_DISABLENODEREUSE=1` / `MSBUILDDISABLENODEREUSE=1` — forces fresh MSBuild workers
   - `UseSharedCompilation=false` — disables compiler server (VBCSCompiler)

   After the build completes, collects all `.nettrace` files created after start time, scans each for CodeAnalysis events, keeps the relevant one(s). Multiple files → `.nettrace.zip`.

### Why MSBuild Complexity Matters

`dotnet build` spawns MSBuild worker processes for compilation. Source generators run inside these workers, not the top-level `dotnet` process. This means:
- EventPipe tracing the top-level process captures nothing useful
- Must ensure child processes inherit tracing env vars
- MSBuild node reuse and the compiler server must be disabled so fresh processes are created

### Trace File Formats

- `.etl` — Windows ETW trace (from system-wide recording). Read via `TraceLog.OpenOrConvert`.
- `.etlx` — Converted ETL format. Read via `TraceLog.OpenOrConvert`.
- `.nettrace` — EventPipe trace (from `--pid` or `-- <command>`). Read via `EventPipeEventSource`.
- `.nettrace.zip` — Bundle of multiple `.nettrace` files from multi-process builds. Extracted and processed individually.
- `.etl.zip` — Compressed ETL file (commonly produced by PerfView). The ETL is streamed from the zip to a temp file using buffered I/O to avoid loading the entire file into memory, then processed via `TraceLog.OpenOrConvert`.

The analyzer dispatches by filename pattern: `.etl.zip` is checked first (full name match), then `.zip` (for nettrace bundles), then `.nettrace`, then everything else falls through to the ETL/ETLX path.

**Note:** `TraceEvent` APIs (`ETWTraceEventSource`, `TraceLog`) only accept file paths, not streams. For zipped formats we must extract to a temp file first. We use 81KB buffered streaming to keep memory usage low even for multi-GB ETL files.

## Build and Test

```powershell
dotnet build           # Build all projects
dotnet test            # Run all tests (47 currently)
```

## Versioning and Publishing

- Version is in `Directory.Build.props` (`<Version>`)
- `PackAsTool=true` must be in each tool `.csproj` (not in props — props evaluate before csproj properties are set)
- Pre-release versions require `--version` flag for `dotnet tool install` and `@version` suffix for `dnx`
- Publish with: `.\publish.ps1 -NuGetKey "key"` (or `-SkipTests`)
- Always bump the version before re-publishing

## Code Style

- File-scoped namespaces
- Primary constructors where appropriate
- `var` when type is obvious
- Collection expressions (`[]`)
- xUnit with `[Fact]` / `[Theory]`, Arrange-Act-Assert pattern
- Public types use PascalCase, private fields use `_camelCase`

## Reference Repositories

The event processing logic was adapted from:
- [GeneratorTracer](https://github.com/chsienki/generatortracer) — Real-time console viewer
- [GeneratorETWViewer](https://github.com/chsienki/generatoretwviewer) — PerfView plugin with detailed execution analysis

## Common Pitfalls

- `TraceEventSession` with a filename creates a file-based session that **cannot** use `.Source` for callbacks. Use a separate real-time session for live event counting.
- `DOTNET_EventPipeConfig` keywords must be 64-bit hex (`0xFFFFFFFFFFFFFFFF`), not 32-bit.
- `DOTNET_EventPipeConfig` accepts multiple providers comma-separated: `Provider1:keywords:level,Provider2:keywords:level`.
- `PackAsTool` in `Directory.Build.props` with `Condition="'$(OutputType)' == 'Exe'"` does NOT work because OutputType isn't set yet when props evaluate. Put `PackAsTool` in each tool's `.csproj`.
- The `Table` record in Models.cs is named `StateTable` to avoid collision with `Spectre.Console.Table`.
- EventPipe `EventPipeEventSource` and ETW `TraceLog` have the same event payloads but different event names (see table above).
- **UAC elevation on Windows:** When self-elevating via `runas`, the elevated process opens a new console window that closes on exit. The `--elevated` sentinel flag is passed so the elevated process wraps execution in try/catch with "Press Enter to close" — otherwise errors are invisible. Use `--verbose` to see the exact exe path and arguments used for elevation.
- **No `.nettrace` merge API exists.** There is no way to programmatically write or merge `.nettrace` files. When multiple processes produce trace files with generator events (multi-project builds), they are zipped into a `.nettrace.zip` bundle instead. The analyzer handles zip bundles transparently.
- **`TraceEvent` has no stream-based APIs.** `ETWTraceEventSource` and `TraceLog` only accept file paths. For `.etl.zip` files, we must extract to a temp file first.

## Publishing a New Version

When asked to publish or release a new version, **start by asking the user for their NuGet API key** using the ask_user tool. Store it for use in step 5. Then follow these steps in order:

### 1. Ask for the NuGet API key

Before doing any work, ask the user for their NuGet API key. You will need it in step 5.

### 2. Bump the version

Edit `Directory.Build.props` and update the `<Version>` element. Use [SemVer](https://semver.org/):
- Pre-release: `0.0.X-alpha` (current phase)
- First stable: `1.0.0`

### 3. Update version references in docs

All `dnx` commands in `README.md`, `docs/recording-guide.md`, and `.github/copilot-instructions.md` include `@version` (required for pre-release). Update them:

```powershell
$old = '0.0.7-alpha'  # previous version
$new = '0.0.8-alpha'  # new version
foreach ($f in @('README.md', 'docs\recording-guide.md', '.github\copilot-instructions.md')) {
    (Get-Content $f -Raw) -replace [regex]::Escape($old), $new | Set-Content $f -NoNewline
}
```

Also update the `dotnet tool install --version` commands in `README.md`, and the example versions in this publishing skill section.

### 4. Run tests

```powershell
dotnet test
```

All tests must pass before publishing.

### 5. Commit the version bump

```
git add -A
git commit -m "Bump version to X.Y.Z-alpha"
git push
```

### 6. Publish to NuGet

Use the NuGet API key obtained in step 1:

```powershell
.\publish.ps1 -NuGetKey "key-from-step-1" -SkipTests
```

This script will:
1. Clean and build in Release
2. Run tests (skipped since already verified in step 4)
3. Pack both tools to `artifacts/packages/`
4. Push to nuget.org

### 6. Verify

After publishing, verify the packages work:

```powershell
# Clear local tool cache
dotnet tool uninstall -g GeneratorLog 2>$null
dotnet tool uninstall -g GeneratorLog.Analyze 2>$null

# Install and test
dotnet tool install -g GeneratorLog --version X.Y.Z-alpha
dotnet tool install -g GeneratorLog.Analyze --version X.Y.Z-alpha
generatorlog --help
generatorlog-analyze --help
```

### Checklist

- [ ] NuGet API key obtained from user
- [ ] Version bumped in `Directory.Build.props`
- [ ] Version updated in all `dnx` commands in README.md
- [ ] Version updated in all `dnx` commands in docs/recording-guide.md
- [ ] Version updated in .github/copilot-instructions.md (example versions)
- [ ] Version updated in `dotnet tool install` commands in README.md
- [ ] Tests pass
- [ ] Changes committed and pushed
- [ ] `publish.ps1` run successfully with NuGet key
- [ ] Packages verified with `dotnet tool install`
