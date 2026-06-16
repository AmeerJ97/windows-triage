# Windows 11 Overheating Research Notes

Date: 2026-06-12

Research question:

What Windows 11 data sources and diagnostic methods should this mini-application use to collect enough evidence for remote overheating and high-CPU triage?

Decision supported:

Design the first full application version from the current PowerShell draft.

Freshness requirement:

Use current official Windows and PowerShell documentation where possible, plus primary project documentation for optional hardware sensor integrations.

## Source Quality

Primary source lanes used:

- Microsoft Learn for WMI/CIM classes, PowerShell cmdlets, event log guidance, powercfg, thermal diagnostics, and CPU troubleshooting.
- LibreHardwareMonitor GitHub repository for optional open-source hardware sensor collection.

Secondary/community sources were only used as search clues and are not treated as authoritative requirements.

## Key Findings

### Native Windows CPU and system inventory is available through WMI/CIM

`Win32_Processor` exposes processor properties including `CurrentClockSpeed`, `LoadPercentage`, `MaxClockSpeed`, core counts, logical processor counts, manufacturer, name, and architecture. This supports baseline CPU inventory and coarse load reporting.

Source:

- https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-processor

Design implication:

- Keep CPU inventory collection.
- Treat `LoadPercentage` and clock values as snapshots, not proof of root cause.
- Correlate snapshot values with sampled counters and event logs before issuing confident diagnoses.

### Native Windows temperature readings are limited

Microsoft documents `Win32_TemperatureProbe`, but notes that most information comes from SMBIOS and current real-time readings are not populated by current WMI implementations. The draft uses `MSAcpi_ThermalZoneTemperature`, which is a common practical source but not a reliable per-core CPU sensor.

Source:

- https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-temperatureprobe

Design implication:

- Do not promise accurate CPU core temperature from Windows built-ins alone.
- Label ACPI readings clearly.
- Include confidence levels:
  - `high`: external sensor backend returns CPU package/core temperatures.
  - `medium`: thermal-zone events or ACPI readings show high thresholds/critical events.
  - `low`: only indirect high-load/throttling evidence is available.

### Windows thermal event logs are important evidence

Microsoft thermal diagnostics list:

- Kernel-Power event 125: ACPI thermal zone being enumerated at boot.
- Kernel-Power event 86: system shutdown due to a critical thermal event.

Source:

- https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/examples--requirements-and-diagnostics

Design implication:

- Event 86 should be a high-severity diagnosis finding.
- Event 125 should be collected to capture ACPI thermal-zone threshold metadata, but should not automatically be treated as a failure because it can be logged during boot.

### Kernel-Processor-Power event 37 is a throttling/platform-limit signal

Microsoft states that event ID 37 is logged when the hardware platform determines that the OS cannot use some processor frequency range. It can indicate firmware or platform power capping/limiting.

Source:

- https://learn.microsoft.com/en-us/troubleshoot/windows-server/setup-upgrade-and-drivers/event-id-37-windows-kernel-processor-power

Design implication:

- Event 37 should trigger a finding such as "firmware/platform is limiting processor speed."
- Recommended next steps should include checking OEM power/thermal settings, BIOS/UEFI updates, and OEM control utilities.
- Do not say event 37 always means overheating; it can also come from power capping or firmware policy.

### High CPU troubleshooting should capture short, bounded performance evidence

Microsoft guidance for high CPU usage recommends collecting performance monitor data with a 1-second to 5-second snapshot interval and collecting WPR logs while high CPU is occurring. It also notes WPR logs should not run for long because files grow quickly.

Source:

- https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-high-cpu-usage-guidance

Design implication:

- Default app should collect a bounded CPU sample, for example 60 seconds at 2-second or 5-second intervals.
- Advanced mode can offer a WPR trace, but default mode should avoid large ETW traces.
- Include per-process CPU deltas, not just cumulative process CPU time.

### PowerShell performance counters are Windows-only and useful for live sampling

`Get-Counter` gets performance counter data from Windows performance monitoring instrumentation and is Windows-only.

Source:

- https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.diagnostics/get-counter

Design implication:

- PowerShell implementation is appropriate for Windows 11.
- Code should gracefully handle missing/corrupt counters.
- If localized counter names become a problem, consider CIM-based fallbacks or typeperf/logman alternatives.

### Event log queries should use server-side filtering where practical

Microsoft documents `Get-WinEvent -FilterHashtable` for efficient event log queries.

Source:

- https://learn.microsoft.com/en-us/powershell/scripting/samples/creating-get-winevent-queries-with-filterhashtable

Design implication:

- Replace "read latest N then filter" with time-bounded `FilterHashtable` queries.
- Query providers and event IDs directly where possible.
- Store enough event metadata in the report: log, provider, ID, level, time, message excerpt, and event XML/properties when useful.

### `powercfg` can produce valuable power diagnostics

Microsoft documents `powercfg` options including:

- `/energy`: analyzes common energy-efficiency and battery-life problems.
- `/batteryreport`: generates a battery usage report.
- `/sleepstudy`: generates diagnostic system power transition report.
- `/srumutil`: dumps System Resource Usage Monitor energy estimation data.

Source:

- https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options

Design implication:

- Default app should run:
  - `powercfg /batteryreport /output <path>` on laptops.
  - `powercfg /energy /duration 60 /output <path>` in normal or deep mode.
  - `powercfg /requests` and `powercfg /a` as text outputs.
- Consider `sleepstudy` only when supported and relevant.
- Keep generated HTML files in the zip bundle, with text summaries extracted or referenced.

### PowerShell execution policy affects usability

PowerShell execution policy controls conditions for loading configuration and running scripts and is a safety feature. It can be set for different scopes, including process scope.

Source:

- https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies

Design implication:

- README should explain safe one-session execution:
  - run PowerShell as Administrator when deeper collection is desired,
  - use a process-scoped bypass only for that invocation if needed,
  - avoid instructing users to permanently weaken system policy.
- Longer term, provide signed releases or packaged executable builds.

### Optional sensor integration can improve diagnosis

LibreHardwareMonitor is open source, licensed under MPL 2.0, and monitors temperature sensors, fan speeds, voltages, load, and clock speeds.

Source:

- https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

Design implication:

- Consider optional integration, not a hard dependency.
- Good architecture:
  - built-in collectors require no download,
  - optional `-UseLibreHardwareMonitor` mode can read bundled or user-provided `LibreHardwareMonitorLib.dll`,
  - license and attribution must be handled if bundled.
- If not bundled, document how to run with external sensor data.

## Recommended Collector Set

### Always collect

- Tool version and run metadata.
- Admin/elevation status.
- Windows version/build/edition.
- Manufacturer, model, serial redaction policy, BIOS version/date.
- CPU inventory and snapshot load/clock.
- Memory capacity and pressure.
- Storage capacity and basic health where available.
- GPU names and driver versions.
- Battery presence and current charge.
- Power plan and selected power settings.
- Recent thermal, power, WHEA, bugcheck, and unexpected shutdown events.
- Current and sampled CPU/memory/disk counters.
- Process CPU deltas over time.
- Top memory, disk, and handle/thread count processes.
- Startup items and running services with privacy-conscious fields.
- Installed hotfix/update summary.
- Defender status and scan/update indicators when accessible.

### Collect in deep mode

- `powercfg /energy` HTML report.
- `powercfg /batteryreport` HTML report.
- `powercfg /sleepstudy` where supported.
- Driver inventory for chipset/GPU/storage/network/system devices.
- Optional process command lines and executable paths.
- Optional WPR trace instructions or capture, only with clear file-size warning.

### Avoid by default

- Full network adapter IP details.
- Full command lines for all processes.
- Full installed software list unless needed.
- Any credential, browser history, user document names, or broad filesystem inventory.

## Diagnosis Rules To Implement

Rules should produce findings with:

- severity: `critical`, `warning`, `info`
- confidence: `high`, `medium`, `low`
- evidence: source values/events
- suggested next action

Initial rules:

- Critical thermal shutdown: Kernel-Power event 86 present.
- Firmware/platform throttling: Kernel-Processor-Power event 37 present.
- Sustained high CPU: average CPU above threshold during sample period.
- Runaway process: one process has high CPU delta across multiple samples.
- High interrupt time: processor interrupt time is elevated, suggesting driver/device issue.
- High memory pressure: low available memory or high committed memory.
- Full system drive: drive usage above threshold.
- Battery degradation: large gap between design/full-charge capacity from battery report, if available.
- BIOS likely stale: BIOS date is old and model/OEM known; present as "check OEM support" rather than "update now."
- Native temperature unavailable: emit an info finding so the triager knows not to expect CPU package temperature.

## Unresolved Gaps

- Need validation on an actual Windows 11 machine.
- Need decide whether to build pure PowerShell, PowerShell plus WinForms/WPF, or package as executable.
- Need decide whether to include LibreHardwareMonitor as optional DLL integration.
- Need define privacy redaction policy and GitHub issue template.
- Need tests for parser/linting once PowerShell is available in the development environment.

## Source List

- Microsoft, `Win32_Processor`: https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-processor
- Microsoft, `Win32_TemperatureProbe`: https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-temperatureprobe
- Microsoft, `powercfg` command-line options: https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options
- Microsoft, `Get-Counter`: https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.diagnostics/get-counter
- Microsoft, Kernel-Processor-Power event 37: https://learn.microsoft.com/en-us/troubleshoot/windows-server/setup-upgrade-and-drivers/event-id-37-windows-kernel-processor-power
- Microsoft, high CPU troubleshooting guidance: https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-high-cpu-usage-guidance
- Microsoft, thermal diagnostics events 86 and 125: https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/examples--requirements-and-diagnostics
- Microsoft, `Get-WinEvent -FilterHashtable`: https://learn.microsoft.com/en-us/powershell/scripting/samples/creating-get-winevent-queries-with-filterhashtable
- Microsoft, PowerShell execution policies: https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies
- LibreHardwareMonitor GitHub repository: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
