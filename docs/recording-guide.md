# How to Record a Generator Log

This guide will help you capture a trace of Roslyn source generator activity during a build. The resulting trace file can be shared for analysis.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later installed
- **Windows:** Administrator access (for system-wide ETW tracing)
- **macOS/Linux:** No special permissions needed (uses EventPipe, per-process)

---

## Windows

On Windows, the tool captures events **system-wide** — you don't need to know the process ID of your build. Just start recording, build normally in another window, and stop when you're done.

### 1. Open a terminal

Open **Windows Terminal**, **PowerShell**, or **Command Prompt**.

> **Tip:** You don't need to open it as Administrator — the tool will prompt you to elevate automatically.

### 2. Navigate to your project directory

```
cd C:\path\to\your\project
```

### 3. Start recording

```
dnx generatorlog
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

On non-Windows platforms, the tool traces a specific process via EventPipe. The easiest approach is to use `--` to wrap your build command — the tool launches it and traces automatically.

### Option A: Wrap a build command (recommended)

```bash
dnx generatorlog -- dotnet build
```

The tool launches `dotnet build`, attaches tracing immediately, records all generator events, and stops when the build completes. The trace is saved to `generators.nettrace`.

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
dnx generatorlog --pid <pid>
```

Press **Ctrl+C** to stop, or it stops automatically when the process exits.

### Share the trace file

Send the resulting `.nettrace` file to whoever requested the trace. If you run the tool multiple times, it creates `generators (1).nettrace`, `generators (2).nettrace`, etc.

---

## Troubleshooting

| Problem | Solution |
|---|---|
| `dnx` is not recognized | Ensure .NET 10 SDK or later is installed. Run `dotnet --version` to check. |
| UAC prompt doesn't appear (Windows) | Right-click your terminal and choose **Run as administrator**, then try again. |
| Counter stays at 0 during build (Windows) | Ensure you're building a project that uses source generators. |
| Permission denied (Windows) | Close other tracing tools (PerfView, Event Viewer) that may hold the ETW session. |
| "Could not connect to process" | Ensure the target is a .NET process and is still running when you attach. |
| "Process exited before tracing could start" | The build finished too quickly. Try `-- dotnet watch build` or a larger project. |

## Advanced Options

Wrap a build command (any platform):

```bash
dnx generatorlog -- dotnet build
dnx generatorlog --output ~/traces/mybuild.nettrace -- dotnet build
```

Save the trace to a specific location:

```bash
# Windows (ETW)
dnx generatorlog --output C:\traces\mybuild.etl

# Any platform (EventPipe)
dnx generatorlog --pid <pid> --output ~/traces/mybuild.nettrace
```

On Windows, you can also trace a specific process via EventPipe instead of system-wide ETW:

```
dnx generatorlog --pid 12345
```

For full help:

```
dnx generatorlog --help
```
