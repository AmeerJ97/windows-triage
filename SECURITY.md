# Security Policy

Windows Triage is a local, read-only diagnostic collector. It does not upload
reports or make network requests by design.

## Supported Versions

Security fixes are applied to the latest public release and the default branch.

## Reporting A Vulnerability

Please report security issues privately to the repository maintainer before
opening a public issue.

If this repository is hosted on GitHub, use GitHub private vulnerability
reporting when it is enabled. Otherwise, contact the maintainer through the
private security contact listed in the repository profile or organization
contact details.

Do not include full diagnostic bundles in public issues. Full bundles can contain
hardware identifiers, device names, driver names, event messages, or other
machine-specific details. Prefer `public_summary.md` for public GitHub issues.

## Report Privacy

Default reports omit:

- local Windows username fields,
- local computer name,
- network addressing details,
- process/service command lines,
- process executable paths.

Users can explicitly opt into some deeper details for troubleshooting. Review
`diagnostic_report.txt` and `diagnostic_data.json` before sharing publicly.
