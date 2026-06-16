## Summary

- describe the change

## Why

- explain the problem being solved

## Validation

- `dotnet restore`
- `dotnet test`
- `opengrep scan --config auto --error --exclude='bin' --exclude='obj' src tests`
- `sonar-scanner` or SonarScanner for .NET, when available
- any manual GUI or CLI checks

## Privacy and security review

- does this change collected data?
- does this change default report contents?
- does this affect elevation, packaging, or release behavior?
- does this preserve `public_summary.md` as the public issue sharing path?

## Notes for reviewers

- screenshots, sample outputs, or migration notes if relevant
