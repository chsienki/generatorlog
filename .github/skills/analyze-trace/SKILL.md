---
name: analyze-trace
description: >
  Analyze a Roslyn source generator trace file (.etl, .etl.zip, .etlx, .nettrace,
  or .nettrace.zip) using the GeneratorLog.Analyze tool in this repo, and produce
  per-generator, per-run, or filtered summaries (e.g. markdown tables for issues).
  Use when the user asks to "analyze a trace", "look at an ETL", "look at a nettrace",
  "run the analyzer", "run the log viewer", or otherwise wants insight into how
  long source generators took inside a captured trace.
---

# Analyze a generator trace

This skill drives the in-repo analyzer (`src/GeneratorLog.Analyze/`) to answer
performance questions about a generator trace file the user points you at.

## When to use

The user has a trace file and wants to know:

- Which generators ran and how long they took.
- The cost of one specific generator (every individual invocation).
- Whether a generator is significant compared to others.
- Cumulative / max / average timings, formatted for an issue or report.

Trace inputs the analyzer accepts (dispatch order matters — see
`src/GeneratorLog.Analyze/Program.cs`):

1. `*.etl.zip` — compressed ETL (e.g. from PerfView). Streamed to a temp file.
2. `*.zip` — bundle of multiple `.nettrace` files.
3. `*.nettrace` — raw EventPipe trace.
4. Anything else — opened via `TraceLog.OpenOrConvert` (`.etl`, `.etlx`).

## Workflow

### 1. Build the analyzer (Release) if it isn't already built

```powershell
dotnet build src\GeneratorLog.Analyze -c Release --nologo -v q
```

The exe lands at:

```
src\GeneratorLog.Analyze\bin\Release\net10.0\GeneratorLog.Analyze.exe
```

Prefer invoking the exe directly rather than `dotnet run` — it skips the MSBuild
spinner noise that pollutes large console outputs.

### 2. Get the high-level summary

```powershell
$exe = "D:\projects\generatorlog\src\GeneratorLog.Analyze\bin\Release\net10.0\GeneratorLog.Analyze.exe"
& $exe "<path-to-trace>"
```

This prints:

- Summary box (driver runs / generator executions / unique generators / processes).
- Per-process overview table.
- Per-generator stats table (count, min/avg/max ms, cumulative, project).
- A `Note:` footer **only if** the trace also contains events from
  `Microsoft-DotNet-SDK-Razor-SourceGenerator`. Absence of the footer means the
  Razor-specific provider was not enabled at capture time — Razor generator
  *outer* timings still show up via the Roslyn `SingleGeneratorRunTime` events
  on `Microsoft-CodeAnalysis-General`, but per-phase Razor breakdowns will not.

### 3. For per-execution detail, export CSV

The Spectre.Console table only shows aggregates. For individual run timings (or
to filter / group / format yourself), use `--csv`:

```powershell
$csv = "$env:TEMP\generator_runs.csv"
& $exe "<path-to-trace>" --csv $csv
```

CSV columns (see `src/GeneratorLog.Analyze/CsvExporter.cs`):

```
process,generator_name,generator_assembly,run_id,project,start_time,execution_time_ms
```

### 4. Reshape with PowerShell for the question being asked

#### All invocations of one generator, as a markdown table

```powershell
$rows = Import-Csv $csv |
    Where-Object { $_.generator_name -like "*Razor*" } |
    Sort-Object { [DateTime]::Parse($_.start_time) }

$total = 0.0; $i = 1
"| # | Start | Duration (ms) |"
"|---:|:---|---:|"
foreach ($r in $rows) {
    $ms = [double]$r.execution_time_ms
    $total += $ms
    "| $i | $($r.start_time) | $($ms.ToString('F2')) |"
    $i++
}
$avg = $total / $rows.Count
"| | **Total** | **$($total.ToString('F2'))** |"
"| | **Average** | **$($avg.ToString('F2'))** |"
```

#### Per-generator summary table sorted by total cost

```powershell
$grouped = Import-Csv $csv | Group-Object generator_name | ForEach-Object {
    $d = $_.Group | ForEach-Object { [double]$_.execution_time_ms }
    [PSCustomObject]@{
        Name  = $_.Name
        Count = $_.Count
        Max   = ($d | Measure-Object -Maximum).Maximum
        Avg   = ($d | Measure-Object -Average).Average
        Total = ($d | Measure-Object -Sum).Sum
    }
} | Sort-Object Total -Descending

"| Generator | Runs | Max (ms) | Avg (ms) |"
"|:---|---:|---:|---:|"
$totalRuns = 0; $totalMax = 0.0; $totalSum = 0.0
foreach ($g in $grouped) {
    "| ``$($g.Name)`` | $($g.Count) | $($g.Max.ToString('F2')) | $($g.Avg.ToString('F2')) |"
    $totalRuns += $g.Count
    if ($g.Max -gt $totalMax) { $totalMax = $g.Max }
    $totalSum += $g.Total
}
$overallAvg = $totalSum / $totalRuns
"| **Total** | **$totalRuns** | **$($totalMax.ToString('F2'))** | **$($overallAvg.ToString('F2'))** |"
```

Adapt the projection to whatever columns the user asks for (e.g. include a
"Total (s)" column, drop Max, etc.). Always emit raw markdown when the user
plans to paste into an issue.

## Things to remember

- **Two providers, two scopes of detail.** Generator outer timings (start/stop
  per generator per driver run) come from `Microsoft-CodeAnalysis-General`.
  Razor-specific phase events come from `Microsoft-DotNet-SDK-Razor-SourceGenerator`,
  which is *only* present if the recording tool enabled it. The summary footer
  is the easiest way to confirm.
- **Same generator may appear twice** in the per-generator table. That happens
  when two different assembly versions of the same generator were loaded over
  the lifetime of the process (e.g. Visual Studio loading an SDK copy and a
  NuGet copy). Group by `generator_name` (CSV) when the user wants one row
  per logical generator; keep both rows when they want to see the version split.
- **Big traces.** `.etl.zip` files can be multi-GB. The analyzer already
  streams to a temp file; just be patient (initial_wait of 300s is reasonable).
- **`<unknown project>` is normal** for ETW recordings of long-lived processes
  like DevHub / Visual Studio — the events don't always carry per-compilation
  project info.
- **Don't reach for `dotnet run`** when piping or capturing output; it interleaves
  the MSBuild progress spinner with program output and makes filtering painful.
- **CSV export is the escape hatch.** Anything you can't get from the Spectre
  console tables (per-run breakdowns, custom groupings, alternative sorts,
  filters) is one `Import-Csv` away.
