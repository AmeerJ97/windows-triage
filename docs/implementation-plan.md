# Implementation Plan

Date: 2026-06-12

## Goal

Build Windows Triage as a public, read-only Windows 11 health diagnostic app that can be downloaded as one executable, launched by double-clicking, and used by non-technical users without installing dependencies.

## Product Shape

Primary release artifact:

- `WindowsTriage.exe`
- Self-contained single-file .NET desktop app.
- WinForms GUI by default.
- CLI mode when arguments are supplied.
- UAC relaunch for collection while allowing `--help` and `--version` without elevation.

The original PowerShell script remains as a prototype/reference implementation while the modular .NET app becomes the maintained product.

## Design Principles

- Safe by default: collect evidence, do not change settings.
- Public/community framing: no private support context in docs or UI.
- Privacy-conscious: omit network addresses, command lines, local usernames, and machine names from default reports.
- Native-first: use Windows APIs and built-in tools rather than requiring installs.
- Modular: collectors, diagnosis rules, reports, GUI, and CLI are separate layers.
- Shared core: GUI and CLI call the same `WindowsTriage.Core` service path.
- Graceful degradation: missing WMI classes, counters, event logs, or permissions become warnings, not crashes.

## Architecture

- `WindowsTriage.Core`
  - Models, collectors, diagnosis rules, report writers, archive writer, and `TriageRunner`.
- `WindowsTriage.App`
  - WinForms GUI, CLI parsing, UAC manifest, and single-file publish configuration.
- `WindowsTriage.Tests`
  - Unit tests for diagnosis behavior and future parser/report tests.

## User-Facing Modes

GUI:

- Default when double-clicked or launched without arguments.
- Presents scan profile, privacy toggles, progress, findings, and report actions.

CLI:

```text
WindowsTriage.exe collect [--profile quick|full|advanced] [--output DIR]
                          [--include-network] [--include-command-lines]
                          [--include-machine-name]
                          [--no-zip] [--json] [--quiet] [--verbose]
WindowsTriage.exe gui
WindowsTriage.exe --help
WindowsTriage.exe --version
```

Profile defaults are Quick = 20 seconds, Full = 60 seconds, and Advanced = 120 seconds. An explicit `--sample-seconds` value overrides those defaults for any profile.

## Collector Scope

V1 collects:

- Run metadata and elevation status.
- System, OS, BIOS, manufacturer/model.
- CPU, memory, GPU, storage, battery, and native thermal-zone data.
- Power configuration and selected `powercfg` outputs.
- Recent thermal, throttling, power, WHEA, bugcheck, and unexpected shutdown events.
- Live performance sampling and process CPU deltas.
- Services, startup items, hotfixes, Defender status, optional network details, and focused driver inventory in Advanced Scan.

## Diagnosis Rules

Each finding includes:

- `Id`
- `Severity`
- `Confidence`
- `Category`
- `Title`
- `Evidence`
- `Recommendation`

Initial rules:

- `THERMAL_SHUTDOWN_EVENT`
- `FIRMWARE_CPU_LIMIT`
- `UNCLEAN_SHUTDOWN`
- `WHEA_HARDWARE_ERROR`
- `SUSTAINED_HIGH_CPU`
- `RUNAWAY_PROCESS`
- `HIGH_INTERRUPT_TIME`
- `MEMORY_PRESSURE`
- `LOW_DISK_SPACE`
- `NATIVE_THERMAL_READING_HIGH`
- `TEMPERATURE_UNAVAILABLE`
- `NO_OBVIOUS_CAUSE`

## Output Layout

```text
WindowsTriage_<Timestamp>_<Id>/
  diagnostic_report.txt
  diagnostic_data.json
  summary.md
  public_summary.md
  logs/
    powercfg_a.txt
    powercfg_requests.txt
    powercfg_query_processor.txt
    powercfg_energy.html
    battery_report.html
WindowsTriage_<Timestamp>_<Id>.zip
```

## Validation Plan

On Windows 11:

- `dotnet restore`
- `dotnet test`
- `dotnet publish .\src\WindowsTriage.App\WindowsTriage.App.csproj -c Release -r win-x64`
- Double-click published EXE and verify UAC prompt, GUI scan, report folder, and zip.
- Run CLI scan with `collect --profile quick --no-zip`.
- Run Advanced Scan and verify optional driver/power reports.
- Confirm network, command-line, and machine-name fields are omitted unless explicitly requested.

## Deferred

- Code signing.
- Win-arm64 publish artifact.
- Optional LibreHardwareMonitor integration.
- HTML report rendering.
- Guided repair actions.
