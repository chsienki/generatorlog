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

On non-Windows platforms, the tool uses EventPipe which traces a **specific process** by its process ID (PID). This means you need to start the build first, then attach.

### Option A: Tracing a `dotnet watch` or long-running build server

This is the easiest approach — attach to a process that stays running.

#### 1. Start your long-running process

In one terminal:

```bash
dotnet watch build
```

#### 2. Find the process ID

In another terminal:

```bash
dotnet build-server status
# or
ps aux | grep dotnet
```

Look for the `dotnet` process corresponding to your build. Note its PID.

#### 3. Start recording

```bash
dnx generatorlog --pid <pid>
```

#### 4. Trigger builds

Make changes and let `dotnet watch` rebuild. The tool records all generator events from the target process.

#### 5. Stop recording

Press **Ctrl+C** in the recording terminal (or it stops automatically if the process exits).

Send the resulting `generators.nettrace` file.

### Option B: Tracing a single `dotnet build`

For a one-off build, you can start the build and the recorder in parallel:

```bash
# Start the build in the background and immediately start tracing
dotnet build &
dnx generatorlog --pid $!
```

The recorder will capture events until the build process exits, then stop automatically.

> **Note:** For very fast builds, the build may complete before the recorder can attach. In that case, use Option A with `dotnet watch` instead.

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
| "Could not connect to process" (macOS/Linux) | Ensure the target is a .NET process and is still running when you attach. |
| "Must specify --pid" (macOS/Linux) | EventPipe requires a target process ID. See the macOS/Linux instructions above. |
| Build completes before recorder attaches (macOS/Linux) | Use `dotnet watch build` so the process stays alive, or try the background `&` approach. |

## Advanced Options

Save the trace to a specific location:

```bash
# Windows
dnx generatorlog --output C:\traces\mybuild.etl

# macOS/Linux
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
