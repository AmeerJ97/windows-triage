# Windows Triage 0.2.0 Beta Release Notes

Windows Triage is a read-only Windows 11 diagnostic collector for overheating,
high CPU, throttling, crashes, battery, storage, drivers, updates, Defender,
and power issues.

## Beta Status

This is a public beta. The executable is unsigned and may trigger Windows
SmartScreen or antivirus warnings. Verify the SHA-256 checksum from the GitHub
release before running the executable.

## What It Does

- Collects local Windows health diagnostics.
- Generates `diagnostic_report.txt`, `diagnostic_data.json`, `summary.md`,
  `public_summary.md`, logs, and a ZIP bundle.
- Does not install software.
- Does not upload data.
- Does not change power settings.
- Does not modify the registry.
- Does not stop services or kill processes.

## Privacy Defaults

Default reports omit:

- local computer name,
- local Windows username,
- network addressing details,
- process and service command lines,
- process executable paths,
- service account names,
- startup item user fields.

Use `public_summary.md` for public GitHub issues. Do not attach the full ZIP
publicly unless specifically requested through a private channel.

## Validation

- `dotnet restore`: pass.
- `dotnet build --configuration Release`: pass, 0 warnings.
- `dotnet test --configuration Release`: pass, 14/14 tests.
- `dotnet publish -c Release -r win-x64`: pass.
- `opengrep scan --config auto`: pass, 0 findings.
- `sonar-scanner`: pass, analysis uploaded.
- Full C# Sonar semantic analysis: pass, analysis uploaded with SonarScanner
  for .NET.
- Windows smoke test: run the GitHub `windows-smoke` workflow before marking
  this beta release ready.

## Known Limitations

- Native Windows temperature data is often incomplete or unavailable.
- The beta executable is unsigned.
- Advanced Scan may record warnings if optional Windows collectors are
  unavailable on a machine.
- GUI UAC behavior still requires real Windows smoke testing before broad
  announcement.
