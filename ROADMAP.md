# Roadmap

This roadmap communicates direction, not guaranteed dates. Safety, privacy,
maintainer capacity, and evidence quality determine priority.

## Now: v0.3 public-beta readiness

- complete signed Windows 11 release-candidate validation;
- finish SignPath Foundation enrollment and signed release automation;
- publish the first checksummed GitHub prerelease;
- collect community feedback on report clarity and false positives;
- improve issue triage and contributor documentation.

## Next

- add fixture-backed parsing for more structured Windows power evidence;
- improve correlation without overstating root cause;
- expand non-English Windows validation;
- investigate a privacy-safe hardware sensor backend;
- improve accessibility and keyboard navigation in the WinForms GUI;
- evaluate a `win-arm64` artifact after the x64 release path is stable.

## Later or exploratory

- HTML report presentation built from the same safe typed data;
- opt-in comparisons between repeated local scans;
- carefully scoped guided troubleshooting actions;
- packaging or Store distribution if community demand justifies it.

## Explicitly out of scope

- automatic repair or silent system-setting changes;
- cloud upload or telemetry by default;
- malware removal or forensic acquisition;
- promises based on unsupported temperature sensors;
- bot-controlled merges or releases.
