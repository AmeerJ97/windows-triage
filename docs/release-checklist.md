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
- Confirm `public_summary.md` is generated and safe to paste publicly.
- Confirm public issue templates request `public_summary.md` or redacted snippets only.

## Package

- Publish `WindowsTriage.exe`.
- Generate checksums.
- Include release notes, unsigned-binary warning, source tag, checksum, and the smoke-test result.
- Mark unsigned beta releases as prerelease.
- Consider code signing before stable distribution.

## Checksums

From Linux:

```bash
./scripts/checksum-release.sh
```

From Windows PowerShell:

```powershell
Get-FileHash .\WindowsTriage.exe -Algorithm SHA256
```
