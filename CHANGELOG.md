# Changelog

All notable changes to Windows Triage will be documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
uses semantic versioning for published releases.

## Unreleased

### Added

- Community governance, support, roadmap, Code of Conduct, and maintainer guidance.
- Path-based PR labeling, first-contributor welcome, dependency review, gentle stale triage, and release drafting.

## 0.3.0-beta.1 - Unreleased candidate

### Added

- Typed, privacy-allowlisted diagnostic sections and schema versioning.
- New stability, storage, battery, Defender, power, and correlation findings.
- Public-summary redaction, privacy manifest, private-artifact consent, and cancellation.
- Elevated Windows, AWS Windows Server, CodeQL, dependency, and signed-release workflows.
- CLI profile shorthand, profile/privacy guidance, printed public summary, and report-folder opening.

### Changed

- Performance sampling now uses language-neutral WMI formatted classes.
- The PowerShell prototype is archived and unsupported.
- Public releases require SignPath signing and post-signing checksums.

### Fixed

- Default report leakage through raw WMI properties and absolute paths.
- GUI summary copying, nonzero command warnings, incomplete-evidence handling, and Windows smoke reliability.

## 0.2.0-beta - 2026-06-16

### Added

- Initial modular .NET 10 WinForms application, CLI, collectors, diagnosis rules, reports, tests, and CI.
