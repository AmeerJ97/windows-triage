# Windows 11 KVM release acceptance

Use the official Windows 11 Enterprise 25H2 evaluation ISO and `scripts/create-windows11-kvm.sh`. This is the authoritative Windows 11 client gate.

## Preconditions

- At least 55 GiB free disk space, working KVM, UEFI, TPM 2.0 emulator, and SPICE display.
- Standard Windows account plus Administrator credentials.
- Signed release candidate downloaded inside the VM through a browser.

## Acceptance

1. Verify the browser download retains Mark-of-the-Web.
2. Verify SmartScreen shows the expected publisher and the Authenticode signature is valid and timestamped.
3. Double-click the executable as the standard user and verify UAC elevation and GUI launch.
4. Run Quick, Full, and Advanced scans and verify reports, privacy manifest, and ZIP creation.
5. Cancel an Advanced scan and verify child work stops and incomplete output is removed.
6. Verify Copy Summary copies `public_summary.md` and Open Report Folder works.
7. Run a default scan and recursively check every report and ZIP entry for machine name, username, user-profile path, serials, device IDs, network addresses, command lines, and raw event messages.
8. Run with all opt-ins and confirm `private/` is retained while `public_summary.md` remains redacted.
9. Enable a non-English Windows display language, reboot, run Quick again, and confirm CPU samples are populated.
10. Record VM version, locale, commit, executable SHA-256, signer, timestamp, outcomes, and screenshots in the release evidence issue.
