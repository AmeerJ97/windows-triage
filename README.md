# Windows Triage

Windows Triage is a read-only Windows 11 health diagnostic application for overheating, high CPU, throttling, crashes, battery, storage, drivers, updates, Defender, and power issues.

The public beta release target is a single Windows executable that a user can download, double-click, approve the Windows Administrator prompt, run a guided scan, and review the generated report bundle locally.

## Current Status

This repository now contains:

- A modular .NET desktop application under `src/`.
- A WinForms GUI for non-technical users.
- A CLI mode for automation and maintainers.
- The original PowerShell prototype, preserved as `Start-WindowsTriage.ps1`.

The PowerShell script is legacy/reference material. New development should target the .NET solution.

## User Experience

For public releases, users should:

1. Download `WindowsTriage.exe` from the release page.
2. Double-click it.
3. Approve the Windows Administrator prompt when the app relaunches for collection.
4. Click `Start Scan`.
5. Share `public_summary.md` in public issues, and keep the full `.zip` report bundle private unless a maintainer requests it through a private channel.

The app does not install anything and does not upload data. Beta binaries may be unsigned; verify release checksums before running them.

## What It Collects

By default, Windows Triage collects:

- Windows version, computer model, BIOS version, uptime, and elevation status.
- CPU model, core/logical processor counts, current clock snapshot, and load snapshot.
- Memory capacity and pressure.
- GPU names and driver versions.
- Storage capacity and basic disk health hints.
- Battery status.
- Native Windows thermal-zone readings when available.
- Power plan and selected `powercfg` outputs.
- Recent event logs related to thermal shutdowns, CPU throttling, power loss, WHEA hardware errors, bugchecks, and unexpected shutdowns.
- A live CPU/memory/disk/interrupt sample.
- Top CPU processes during the sample, calculated from process CPU deltas.
- Running services and startup items.
- Windows Update, recent hotfix, and Microsoft Defender summary when available.

Optional privacy-sensitive data is off by default:

- Network addressing details.
- Process and service command lines.
- Local Windows username.
- Local computer name.

## What It Does Not Do

Windows Triage does not:

- Change power settings.
- Kill processes.
- Disable startup items.
- Stop or restart services.
- Install software.
- Upload data.
- Modify the registry.

It is a collector and diagnosis assistant, not an automatic repair tool.

## Build

Prerequisite for maintainers:

- .NET 10 SDK on Windows 11.

Build and test:

```powershell
dotnet restore
dotnet test
dotnet publish .\src\WindowsTriage.App\WindowsTriage.App.csproj -c Release -r win-x64
```

The publish output contains the self-contained single-file Windows executable.

## CLI

Double-clicking opens the GUI and relaunches elevated for complete collection. Passing arguments enables CLI mode:

```powershell
WindowsTriage.exe collect
WindowsTriage.exe collect --profile quick
WindowsTriage.exe collect --profile advanced --include-network --include-command-lines
WindowsTriage.exe collect --include-machine-name
WindowsTriage.exe collect --output C:\Temp --no-zip --json
WindowsTriage.exe gui
WindowsTriage.exe --help
WindowsTriage.exe --version
```

`--help` and `--version` do not require elevation. `collect` must be run from an Administrator terminal; otherwise it exits with code `3`.

Exit codes:

- `0`: completed successfully.
- `1`: completed with collection warnings.
- `2`: invalid arguments.
- `3`: elevation denied or insufficient access.
- `4`: fatal collection or report failure.

## Reports

Each scan writes:

- `diagnostic_report.txt`: human-readable report with findings at the top.
- `diagnostic_data.json`: structured data for deeper review or future automation.
- `summary.md`: concise findings summary.
- `public_summary.md`: redacted summary intended for public GitHub issues.
- `logs/`: supporting command outputs and optional HTML reports.
- `WindowsTriage_<Timestamp>_<Id>.zip`: local archive for private sharing.

## Interpreting Findings

Each finding has:

- `Severity`: `Critical`, `Warning`, or `Info`.
- `Confidence`: `High`, `Medium`, or `Low`.
- `Category`: thermal, power, performance, storage, memory, hardware, drivers, or general.
- `Evidence`: what the app saw.
- `Recommendation`: what to check next.

Important examples:

- `THERMAL_SHUTDOWN_EVENT`: Windows recorded a critical thermal shutdown.
- `FIRMWARE_CPU_LIMIT`: firmware/platform policy limited CPU speed.
- `SUSTAINED_HIGH_CPU`: CPU usage was high during the live sample.
- `RUNAWAY_PROCESS`: one process used a large CPU share during the sample.
- `HIGH_INTERRUPT_TIME`: possible driver/device issue.
- `WHEA_HARDWARE_ERROR`: hardware error events were found.
- `TEMPERATURE_UNAVAILABLE`: Windows did not expose useful native temperature readings.

Native Windows temperature readings are often incomplete on many systems. Treat them as clues, not perfect CPU package/core readings.

## Repository Notes

Current project docs:

- `docs/investigation.md`
- `docs/windows-11-overheating-research.md`
- `docs/implementation-plan.md`
- `docs/build-and-release.md`
- `docs/windows-smoke-test.md`
- `docs/release-checklist.md`
- `docs/release-notes-0.2.0-beta.md`

The original PowerShell prototype is preserved as `Start-WindowsTriage.ps1` for reference only. New development should target the .NET solution.

## Publishing Checklist

Before publishing a public beta:

- Build and test with the .NET 10 SDK.
- Run the GitHub `windows-smoke` workflow, or smoke test the published EXE on a clean Windows 11 VM without installing .NET runtime.
- Confirm UAC prompt appears on launch.
- Confirm GUI scan, CLI scan, Advanced Scan, and generated zip work.
- Confirm public GitHub issues use `public_summary.md`, not full diagnostic ZIP attachments.
- Publish SHA-256 checksums and clearly label unsigned beta binaries.
- Consider code signing before a stable release.
