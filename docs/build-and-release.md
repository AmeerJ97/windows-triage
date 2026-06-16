# Build And Release

Windows Triage is distributed as a self-contained single-file Windows executable.
Public beta binaries may be unsigned. Release notes must include the SHA-256
checksum, source tag, Windows smoke-test result, and an unsigned-binary warning.

## Maintainer Prerequisites

- Windows 11
- .NET 10 SDK
- PowerShell or Windows Terminal

End users do not need the .NET runtime or SDK when using the published self-contained EXE.

## Build

```powershell
dotnet restore
dotnet test
dotnet publish .\src\WindowsTriage.App\WindowsTriage.App.csproj -c Release -r win-x64
```

Expected publish settings are defined in `src/WindowsTriage.App/WindowsTriage.App.csproj`:

- `TargetFramework=net10.0-windows10.0.22000.0`
- `UseWindowsForms=true`
- `RuntimeIdentifier=win-x64`
- `SelfContained=true`
- `PublishSingleFile=true`
- `EnableCompressionInSingleFile=true`
- `PublishTrimmed=false`

## Smoke Test

On a Windows 11 test machine:

1. Run the published EXE by double-clicking it.
2. Confirm the app relaunches with a UAC prompt before collection.
3. Run a Full Scan.
4. Confirm `diagnostic_report.txt`, `diagnostic_data.json`, `summary.md`, `public_summary.md`, `logs/`, and the zip archive are created.
5. Run:

```powershell
.\WindowsTriage.exe collect --profile quick --no-zip
```

6. Confirm CLI output and exit code.

Use `docs/windows-smoke-test.md` for the full release smoke checklist.

## Release Notes

Before publishing a release:

- Review generated reports for privacy defaults.
- Confirm network and command-line details appear only when explicitly requested.
- Confirm machine names appear only when explicitly requested.
- Confirm `public_summary.md` is safe for public issue sharing.
- Confirm the GitHub `windows-smoke` workflow passes, or confirm the published EXE runs on a clean Windows 11 VM without installing .NET.
- Publish SHA-256 checksums and an unsigned-binary warning for beta artifacts.
- Consider code signing before stable public distribution.
