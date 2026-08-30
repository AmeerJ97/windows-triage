# Build And Release

Windows Triage is distributed as a self-contained single-file Windows executable.
Public binaries must be signed through the approved SignPath Foundation project.
Release notes must include the SHA-256 checksum, source tag, Windows smoke-test
result, AWS smoke evidence, Windows 11 client evidence, and signer identity.

## Maintainer Prerequisites

- Windows 11
- .NET 10 SDK
- PowerShell or Windows Terminal

End users do not need the .NET runtime or SDK when using the published self-contained EXE.

## Build

```powershell
dotnet restore --locked-mode
dotnet test --configuration Release --no-restore
dotnet publish .\src\WindowsTriage.App\WindowsTriage.App.csproj -c Release -r win-x64 --self-contained true --no-restore
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
Use `docs/aws-smoke.md` for supplemental Windows Server evidence and
`docs/windows-11-kvm-smoke.md` for the authoritative Windows 11 client gate.

## Release Notes

Before publishing a release:

- Review generated reports for privacy defaults.
- Confirm network and command-line details appear only when explicitly requested.
- Confirm machine names appear only when explicitly requested.
- Confirm `public_summary.md` is safe for public issue sharing.
- Confirm the GitHub `windows-smoke` workflow passes, or confirm the published EXE runs on a clean Windows 11 VM without installing .NET.
- Run `scripts/aws-windows-smoke.sh` against the release candidate and retain its JSON evidence.
- Complete `docs/windows-11-kvm-smoke.md` against the signed candidate.
- Verify the timestamped Authenticode signature and expected SignPath publisher.
- Publish SHA-256 checksums generated after signing.
