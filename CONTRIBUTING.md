# Contributing to Windows Triage

By participating, you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). Review
the [governance](GOVERNANCE.md), [support boundaries](SUPPORT.md),
[roadmap](ROADMAP.md), and [maintainer guide](docs/maintainer-guide.md) before
proposing a substantial change. Questions and early ideas belong in GitHub
Discussions; actionable bugs and scoped feature requests belong in Issues.

Windows Triage is a Windows-first diagnostic tool for collecting read-only system health data and producing report bundles that a user can review and share.

## Before You Start

- Keep the tool read-only by default.
- Preserve the current privacy posture:
  - network details are opt-in,
  - command lines are opt-in,
  - machine names are opt-in,
  - report sharing should remain user-controlled.
- Prefer changes in the .NET solution under `src/` over the legacy PowerShell prototype.

## Development Environment

Prerequisites:

- Windows 11
- .NET 10 SDK

Common commands:

```powershell
dotnet restore
dotnet test
dotnet publish .\src\WindowsTriage.App\WindowsTriage.App.csproj -c Release -r win-x64
```

## Change Guidelines

- Make focused changes with clear behavior.
- Add or update tests when changing collectors, report generation, CLI parsing, or findings logic.
- Do not introduce background services, telemetry, or automatic uploads.
- Do not enable privacy-sensitive collection by default.
- Document user-visible flags, outputs, and report changes in `README.md`.

## Pull Requests

Please include:

- a short problem statement,
- the approach taken,
- any privacy or report-format impact,
- test coverage notes,
- screenshots for GUI changes when relevant.

If your change affects report contents, call out whether it adds or removes:

- usernames,
- hostnames,
- machine names,
- network details,
- executable paths,
- process or service command lines.

## Security Issues

Do not open a public issue for a suspected security or privacy vulnerability.

Follow the instructions in `SECURITY.md` and report it privately.
