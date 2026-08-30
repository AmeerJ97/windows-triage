# Prototype Investigation

Date: 2026-06-12

## Question

How should the preliminary PowerShell prototype be evolved into a public Windows 11 health diagnostic application?

Scope boundary:

- Preserve the prototype as reference material.
- Build the maintained product as a modular, public-facing application.
- Keep the tool read-only and privacy-conscious.

## Repository State At Start

The project began with:

- `draft_script.ps1`: preliminary PowerShell diagnostic script.
- No application project structure.
- No compiled release path.

The maintained public reference prototype is now `Start-WindowsTriage.ps1`.

The current implementation now adds:

- .NET solution and projects under `src/`.
- WinForms GUI application.
- CLI mode.
- Modular collectors and diagnosis rules.
- Report and archive generation.

## Prototype Strengths

The prototype established useful diagnostic coverage:

- System, OS, BIOS, CPU, memory, GPU, storage, battery, thermal-zone, power, event log, process, service, startup, update, and network collection.
- Text report generation.
- Zip archive creation.
- Early recommendation logic.

These ideas were retained but moved into modular C# services.

## Findings That Shaped The App

### Process CPU needed better measurement

The prototype sorted processes by cumulative CPU time. The .NET app samples process CPU at the start and end of the scan, then calculates CPU deltas to better identify active CPU consumers.

### Native temperature data must be treated as best-effort

Windows built-in thermal-zone readings can be missing or represent ACPI zones rather than exact CPU package/core temperatures. The app labels native readings as clues and corroborates overheating through event logs, throttling, and performance behavior.

### Event collection should be targeted

The app collects relevant event IDs for thermal shutdowns, processor power limits, unexpected shutdowns, WHEA hardware errors, and bugchecks, instead of relying on a small newest-events sample.

### Privacy defaults matter

Network addressing, command lines, machine names, and local user identifiers can reveal private details. The GUI and CLI keep these fields off by default and require explicit opt-in where supported.

### Public distribution needs a compiled app

The prototype required PowerShell knowledge and execution-policy workarounds. The public application should be a single executable that opens a GUI by default and prompts for elevation automatically.

## Current Direction

The maintained product is now:

- `.NET 10`
- `net10.0-windows10.0.22000.0`
- WinForms GUI
- Self-contained single-file `win-x64` publish target
- UAC relaunch for collection
- Modular core used by both GUI and CLI

## Remaining Gaps

- Elevated Quick collection and privacy assertions passed on AWS Windows Server 2025 build 26100 for the v0.3 development candidate.
- Still needs the authoritative interactive Windows 11 KVM result and GitHub-hosted Windows smoke on the release commit.
- SignPath Foundation enrollment and signing remain mandatory before publishing v0.3.
- Needs optional sensor integration decision after v1 stabilizes.
