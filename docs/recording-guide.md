# How to Record a Generator Log

This guide will help you capture a trace of Roslyn source generator activity during a build. The resulting trace file can be shared for analysis.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later installed
- **Windows:** Administrator access (for system-wide ETW tracing), or use `-- <command>` mode which doesn't require admin
- **macOS/Linux:** No special permissions needed (uses EventPipe)

---

## Quick Start (any platform)

The simplest way to capture a trace on any platform:

```
dnx generatorlog@0.0.6-alpha -- dotnet build
```

This launches `dotnet build`, traces all generator events from every process in the build, and saves the result to `generators.nettrace` when the build completes.

> **Tip:** If something isn't working as expected, add `--verbose` to see exactly what the tool is doing:
> ```
> dnx generatorlog@0.0.6-alpha --verbose -- dotnet build
> ```

---

## Windows (system-wide ETW)

On Windows, the tool can also capture events **system-wide** — you don't need to know the process ID of your build. This is useful when building from Visual Studio or other IDEs.

### 1. Open a terminal

Open **Windows Terminal**, **PowerShell**, or **Command Prompt**.

> **Tip:** You don't need to open it as Administrator — the tool will prompt you to elevate automatically.

### 2. Navigate to your project directory

```
cd C:\path\to\your\project
```

### 3. Start recording

```
dnx generatorlog@0.0.6-alpha
```

You should see:

```
Recording generator events (ETW, system-wide) to: C:\path\to\your\project\generators.etl
Press Ctrl+C to stop recording.

Recorded 0 driver run(s), 0 generator execution(s)
```

> **Note:** If you see a Windows UAC prompt asking for administrator permissions, click **Yes**. This is required to enable ETW tracing.

Leave this terminal open and running.

### 4. Build your project

In a **separate** terminal window, build your project as you normally would:

```
dotnet build
```

Or open the solution in **Visual Studio** and build from there.

As builds run, the counter in the recording terminal will update:

```
Recorded 3 driver run(s), 15 generator execution(s)
```

> **Tip:** Run the build several times if you want to capture multiple iterations, or perform the specific action you're investigating (e.g. editing a file and rebuilding).

### 5. Stop recording

Go back to the recording terminal and press **Ctrl+C**.

```
Recording complete. 3 driver run(s), 15 generator execution(s).
Saved to: C:\path\to\your\project\generators.etl
```

### 6. Share the trace file

Send the `generators.etl` file from your project directory to whoever requested the trace.

> **Note:** If you run the tool multiple times, it will create `generators (1).etl`, `generators (2).etl`, etc. to avoid overwriting previous traces.

---

## macOS / Linux

On non-Windows platforms, the tool traces via EventPipe. The recommended approach is to use `--` to wrap your build command.

### Option A: Wrap a build command (recommended)

```bash
dnx generatorlog@0.0.6-alpha -- dotnet build
```

The tool:
1. Launches `dotnet build` with EventPipe tracing enabled
2. Disables MSBuild node reuse and compiler server so all processes are traced
3. Waits for the build to complete
4. Collects all trace files, filters to those with generator events
5. Produces a single output file (`generators.nettrace`, or `generators.nettrace.zip` if multiple processes had events)

> This also works on Windows if you prefer per-process tracing over system-wide ETW.

### Option B: Attach to a running process

For long-running processes like `dotnet watch` or a build server:

#### 1. Start your long-running process

```bash
dotnet watch build
```

#### 2. Find the process ID

```bash
ps aux | grep dotnet
```

#### 3. Attach the recorder

```bash
dnx generatorlog@0.0.6-alpha --pid <pid>
```

Press **Ctrl+C** to stop, or it stops automatically when the process exits.

### Tracing VS Code (C# Dev Kit)

When using VS Code with the C# extension (C# Dev Kit), source generators run inside the **Roslyn language server**, not the `code` process itself. The language server runs as a `dotnet` process with `Microsoft.CodeAnalysis.LanguageServer.dll` in its arguments.

On macOS / Linux, find and attach to it:

```bash
# Find the Roslyn language server process
ps aux | grep Microsoft.CodeAnalysis.LanguageServer

# Attach to it
dnx generatorlog@0.0.6-alpha --pid <pid>
```

### Share the trace file

Send the resulting `.etl`, `.nettrace`, or `.nettrace.zip` file to whoever requested the trace. If you run the tool multiple times, it creates `generators (1).etl`, `generators (1).nettrace`, etc.

---

## Troubleshooting

| Problem | Solution |
|---|---|
| `dnx` is not recognized | Ensure .NET 10 SDK or later is installed. Run `dotnet --version` to check. |
| UAC prompt doesn't appear (Windows) | Right-click your terminal and choose **Run as administrator**, then try again. |
| Counter stays at 0 during build (Windows) | Ensure you're building a project that uses source generators. |
| Permission denied (Windows) | Close other tracing tools (PerfView, Event Viewer) that may hold the ETW session. |
| "Could not connect to process" | Ensure the target is a .NET process and is still running when you attach. |
| No generator events found | Use `--verbose` to see which files were collected and scanned. The build may not use source generators, or the trace files may be in an unexpected directory. |

> **Tip:** When reporting issues or debugging unexpected behavior, run with `--verbose` and include the output.

## Advanced Options

All options:

```
generatorlog [options] [-- <command> [args...]]

Options:
  --output, -o <path>   Output file path
  --pid, -p <pid>       Trace a specific process
  --verbose, -v         Show detailed diagnostic output
  -- <command>          Launch and trace a command
```

Save the trace to a specific location:

```bash
dnx generatorlog@0.0.6-alpha --output ~/traces/mybuild.nettrace -- dotnet build

# Windows (ETW)
dnx generatorlog@0.0.6-alpha --output C:\traces\mybuild.etl
```

On Windows, trace a specific process via EventPipe instead of system-wide ETW:

```
dnx generatorlog@0.0.6-alpha --pid 12345
```

For full help:

```
dnx generatorlog@0.0.6-alpha --help
```
