# Release Checklist

Use this checklist for each public Windows Triage release.

## Version

- Update `Directory.Build.props` version.
- Confirm README examples still match the CLI.
- Confirm `docs/build-and-release.md` target framework and paths are current.

## Quality Gates

```bash
dotnet restore
dotnet test --no-restore
dotnet build --no-restore
dotnet publish ./src/WindowsTriage.App/WindowsTriage.App.csproj -c Release -r win-x64 --no-restore
xmllint --noout Directory.Build.props src/WindowsTriage.Core/WindowsTriage.Core.csproj src/WindowsTriage.App/WindowsTriage.App.csproj src/WindowsTriage.App/app.manifest tests/WindowsTriage.Tests/WindowsTriage.Tests.csproj
opengrep scan --config auto --error --exclude='bin' --exclude='obj' src tests
sonar-scanner
```

For full C# SonarQube semantic analysis, use SonarScanner for .NET:

```bash
dotnet sonarscanner begin /k:"windows-triage" /d:sonar.host.url="$SONAR_HOST_URL" /d:sonar.token="$SONAR_TOKEN"
dotnet build --configuration Release
dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
```

## Windows Smoke

- Complete `docs/windows-smoke-test.md`.
- Run the GitHub `windows-smoke` workflow if no local Windows 11 machine is available.
- Confirm GUI scan succeeds.
- Confirm CLI help/version work without elevation.
- Confirm CLI collection works from an Administrator terminal.
- Confirm privacy defaults hold.
- Confirm the default ZIP has no private artifacts and all-opt-in `public_summary.md` remains redacted.
- Confirm AWS Windows Server and local Windows 11 KVM evidence links are available.
- Confirm `public_summary.md` is generated and safe to paste publicly.
- Confirm public issue templates request `public_summary.md` or redacted snippets only.

## Package

- Publish the SignPath-signed `WindowsTriage.exe`.
- Generate checksums.
- Include release notes, source tag, post-signing checksum, signer identity, and smoke evidence.
- Verify the expected signer, valid timestamp, and post-signing SHA-256 checksum.
- Mark beta releases as prerelease.

## Checksums

From Linux:

```bash
./scripts/checksum-release.sh
```

From Windows PowerShell:

```powershell
Get-FileHash .\WindowsTriage.exe -Algorithm SHA256
```
