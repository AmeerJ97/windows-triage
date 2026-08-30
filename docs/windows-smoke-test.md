# Windows Smoke Test Checklist

Use this checklist before publishing a public Windows Triage release.

## Test Machine

- Windows 11 x64.
- Standard user account plus Administrator credentials.
- No .NET SDK/runtime requirement for the published EXE smoke test.

## Build Artifact

From a build machine:

```powershell
dotnet publish .\src\WindowsTriage.App\WindowsTriage.App.csproj -c Release -r win-x64
```

Copy this file to the Windows test machine:

```text
src\WindowsTriage.App\bin\Release\net10.0-windows10.0.22000.0\win-x64\publish\WindowsTriage.exe
```

## GUI Smoke

1. Double-click `WindowsTriage.exe`.
2. Confirm a UAC prompt appears for collection.
3. Approve UAC.
4. Confirm the GUI opens.
5. Run a `Quick` scan.
6. Confirm the scan completes without crashing.
7. Click `Copy Summary`.
8. Paste into Notepad and confirm findings text appears.
9. Click `Open Report Folder`.
10. Confirm the folder opens.

## Report Files

Confirm the report folder contains:

- `diagnostic_report.txt`
- `diagnostic_data.json`
- `summary.md`
- `public_summary.md`
- `privacy_manifest.json`
- `logs\`
- a sibling `.zip` archive

Confirm the zip opens.

## Privacy Defaults

Run a default `Quick` scan and check `diagnostic_data.json`.

Expected absent by default:

- `userName`
- local computer name
- `Environment.UserName`
- service `StartName`
- startup `User`
- process executable paths under `topCpuProcesses` and `topMemoryProcesses`
- process command lines
- network IP addresses

PowerShell helper:

```powershell
Select-String -Path .\diagnostic_data.json -Pattern 'userName|computerName|StartName|"User"|CommandLine|C:\\Users\\|IPAddress'
```

Expected result: no matches.

Also confirm the default report and ZIP have no `private\` directory. Run a
second collection with all privacy opt-ins and confirm `private\` exists while
`public_summary.md` still omits machine name, username, local paths, network
addresses, command lines, SIDs, and MAC addresses.

## CLI Smoke

From a non-elevated terminal:

```powershell
.\WindowsTriage.exe --help
.\WindowsTriage.exe --version
.\WindowsTriage.exe collect --profile quick --no-zip
```

Expected:

- `--help` exits `0`.
- `--version` exits `0`.
- `collect` exits `3` with an elevation message.

From an Administrator terminal:

```powershell
.\WindowsTriage.exe collect --profile quick --no-zip --output $env:TEMP
```

Expected:

- Exit code is `0` or `1`.
- Report folder is created under `%TEMP%`.
- `diagnostic_report.txt`, `diagnostic_data.json`, and `summary.md` exist.
- `public_summary.md` exists.

## Windows Collector Checks

Inspect `diagnostic_data.json` and `logs\` for:

- WMI system details.
- Event log results or clear collection warning.
- `powercfg` command output files.
- Performance sample summary.
- Top CPU and memory process rows.
- Defender status or clear collection warning.

## Advanced Scan

From the GUI or Administrator CLI, run an Advanced scan.

Expected:

- Driver inventory is included.
- `powercfg_energy.html` is attempted.
- `sleepstudy_report.html` is attempted where supported.
- Failures are recorded as warnings, not crashes.

## Pass Criteria

The release candidate passes smoke testing when:

- GUI launches and completes a scan.
- CLI help/version work without elevation.
- CLI collection works from an Administrator terminal.
- Reports and zip are generated.
- `public_summary.md` can be pasted into a public GitHub issue without machine identity.
- Privacy defaults hold.
- Windows-only collectors either return data or produce friendly warnings.
