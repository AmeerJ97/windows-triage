# Windows Triage 0.3.0 Beta 1

This beta hardens privacy, strengthens diagnosis, and requires signed release artifacts.

## Highlights

- Typed, privacy-allowlisted diagnostic sections with a compatible section layout and schema version.
- Default reports omit machine identity, usernames, hardware IDs, absolute paths, raw event messages, and raw power reports.
- Optional private artifacts are retained only after explicit consent.
- Language-neutral WMI performance sampling replaces localized English counter names.
- New findings cover bugchecks, NTFS corruption, Defender state, battery wear, disk health, missing performance evidence, energy errors, and correlated load/thermal signals.
- GUI cancellation, confidence/evidence details, and safe public-summary copying.
- Elevated Windows smoke, AWS Windows Server smoke, Windows 11 KVM acceptance, SignPath signing, and post-signing checksum gates.

## Compatibility

Existing CLI commands and exit codes remain supported. `diagnostic_data.json` keeps its established section names and adds `SchemaVersion`. Sensitive raw fields have intentionally been removed from default output.

## Deferred

LibreHardwareMonitor integration, ARM64, automatic repair actions, driver-age heuristics, and BIOS-age heuristics remain out of scope.

## Current verification status

- Local tests: 29 passed.
- Release build and self-contained `win-x64` publish: passed with zero warnings.
- NuGet vulnerability audit: no known vulnerable direct or transitive packages.
- AWS Windows Server 2025 elevated Quick collection and default privacy assertions: passed.
- AWS temporary IAM, EC2, security-group, and S3 cleanup: independently confirmed.
- Windows 11 KVM acceptance: pending.
- SignPath signing and signed GitHub Release: pending.
