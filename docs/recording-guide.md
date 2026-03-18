# How to Record a Generator Log

This guide will help you capture a trace of Roslyn source generator activity during a build. The resulting trace file can be shared for analysis.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later installed
- **Windows:** Administrator access (for system-wide ETW tracing)
- **macOS/Linux:** No special permissions needed (uses EventPipe, per-process)

## Steps (Windows)

### 1. Open a terminal

Open **Windows Terminal**, **PowerShell**, or **Command Prompt**.

> **Tip:** You don't need to open it as Administrator — the tool will prompt you to elevate automatically.

### 2. Navigate to your project directory

```
cd C:\path\to\your\project
```

### 3. Start recording

Run:

```
dnx generatorlog
```

You should see output like:

```
Recording generator events (ETW, system-wide) to: C:\path\to\your\project\generators.etl
Press Ctrl+C to stop recording.

Recorded 0 driver run(s), 0 generator execution(s)
```

> **Note:** If you see a Windows UAC prompt asking for administrator permissions, click **Yes**. This is required to enable ETW tracing.

Leave this terminal window open and running.

### 4. Build your project

In a **separate** terminal window, build your project as you normally would:

```
dotnet build
```

Or open the solution in **Visual Studio** and build from there.

As builds run, you should see the counter in the recording terminal update:

```
Recorded 3 driver run(s), 15 generator execution(s)
```

> **Tip:** Run the build several times if you want to capture multiple iterations, or perform the specific action you're investigating (e.g. editing a file and rebuilding).

### 5. Stop recording

Go back to the terminal running `generatorlog` and press **Ctrl+C**.

You'll see a summary:

```
Recording complete. 3 driver run(s), 15 generator execution(s).
Saved to: C:\path\to\your\project\generators.etl
```

### 6. Share the trace file

The file `generators.etl` is in your project directory. Send this file to whoever requested the trace.

## Steps (macOS / Linux)

On non-Windows platforms, the tool uses EventPipe which traces a **specific process** by PID.

### 1. Start your build in the background

```bash
dotnet build &
BUILD_PID=$!
```

### 2. Record the trace

```bash
dnx generatorlog --pid $BUILD_PID
```

The tool will attach to the build process and record events to `generators.nettrace`. Recording stops automatically when the build completes, or you can press **Ctrl+C**.

### 3. For long-running processes (e.g. Visual Studio Code, `dotnet watch`)

Find the process ID first:

```bash
# Find running dotnet processes
ps aux | grep dotnet
```

Then attach:

```bash
dnx generatorlog --pid <pid>
```

### 4. Share the trace file

Send the resulting `.nettrace` file to whoever requested the trace.

## Troubleshooting

| Problem | Solution |
|---|---|
| `dnx` is not recognized | Ensure you have .NET 10 SDK or later installed. Run `dotnet --version` to check. |
| UAC prompt doesn't appear / elevation fails (Windows) | Right-click your terminal and choose **Run as administrator**, then run the command again. |
| Counter stays at 0 during build | Make sure you're building a project that uses source generators. Try `dotnet build` in the same directory where the tool is running. |
| Permission denied errors (Windows) | Close any other tracing tools (PerfView, Event Viewer) that might be using the same ETW session. |
| "Could not connect to process" (EventPipe) | Ensure the target is a .NET process and is still running when you attach. |
| "Must specify --pid" (macOS/Linux) | EventPipe tracing requires a target process ID. See the macOS/Linux steps above. |

## Advanced Options

Save the trace to a specific location:

```
dnx generatorlog --output ~/traces/mybuild.etl
```

On Windows, trace a specific process via EventPipe instead of system-wide ETW:

```
dnx generatorlog --pid 12345
```

For full help:

```
dnx generatorlog --help
```
