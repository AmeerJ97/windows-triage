# Privacy model

Windows Triage collects locally, writes locally, and has no report-upload or telemetry path. Privacy is enforced at collection, typed normalization, report serialization, public-summary redaction, archive creation, and Windows smoke testing.

## Default bundle

The default report bundle omits:

- computer name and local usernames;
- hardware serial numbers, processor IDs, PNP IDs, UUIDs, and MAC addresses;
- absolute user-profile and report paths;
- network addresses and DNS/gateway details;
- process executable paths and command lines;
- service accounts and startup-user fields;
- raw event messages;
- raw battery, energy, and system-power reports.

The safe typed report can still contain hardware models, Windows/BIOS versions, process names without paths, drive letters and capacity, event provider/ID/time, performance values, findings, and collection warnings. These are diagnostically useful but should still be reviewed before sharing.

## Explicit opt-ins

The four privacy switches are independent:

- `--include-machine-name`
- `--include-network`
- `--include-command-lines`
- `--include-private-artifacts`

Private-artifact mode retains raw Windows evidence under `private/`. It does not silently enable the other options. Raw Windows files can contain machine-specific identifiers that the typed reports intentionally exclude.

## Report roles

| Artifact | Sharing policy |
|---|---|
| `public_summary.md` | Intended for public issues after user review. Redacted independently of collection opt-ins. |
| `summary.md` | Local concise summary; may include explicitly requested machine name. |
| `diagnostic_report.txt` | Local detailed report; share privately only after review. |
| `diagnostic_data.json` | Structured local evidence; share privately only after review. |
| `privacy_manifest.json` | Records schema version, enabled opt-ins, excluded categories, and sharing guidance. |
| ZIP bundle | Keep private, especially when `private/` exists. |

Public-summary redaction removes known computer/user identity, user-profile paths, SIDs, MAC addresses, and IPv4 addresses. Redaction reduces risk but is not a substitute for reviewing user-generated process names or diagnostic wording.

## Verification invariant

Default Windows smoke recursively scans every report file and ZIP entry. The release gate rejects machine name, username/profile paths, WMI `systemName`, serial-number, processor-ID, PNP-ID, command-line, and executable-path leakage. An all-opt-in run separately verifies that `public_summary.md` remains redacted.

Privacy defects should be reported privately through GitHub private vulnerability reporting. Never attach a full diagnostic bundle to a public vulnerability report.
