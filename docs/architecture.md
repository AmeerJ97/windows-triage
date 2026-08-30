# Architecture

Windows Triage is a Windows 11 PC-health diagnostic collector. It is not an incident-response or forensic acquisition tool.

## Execution flow

```text
GUI or CLI
  -> CollectionOptions and privacy consent
  -> TriageRunner
  -> typed collectors
  -> diagnosis and correlation rules
  -> text, JSON, summary, public summary, and privacy manifest
  -> optional ZIP
```

The GUI and CLI use the same `WindowsTriage.Core` path. A collector failure becomes a typed warning so independent evidence can continue. Cancellation propagates through collectors and child processes, deletes incomplete current-run output, and removes temporary raw files.

## Typed evidence boundary

Collectors query only explicit WMI fields and normalize them into `TriageSections`. Diagnosis rules consume typed DTOs rather than arbitrary dictionaries. `diagnostic_data.json` retains established section names and adds `SchemaVersion` for compatible evolution.

Absolute output paths are runtime-only and ignored during JSON serialization. Raw command and event artifacts are created in an application-owned temporary directory, parsed into safe typed evidence, discarded by default, and copied into `private/` only after explicit consent.

## Collectors

- Run metadata and selected system/BIOS fields.
- CPU, memory, GPU, storage, battery, and native thermal zones.
- Language-neutral WMI formatted CPU, interrupt, memory, and disk performance samples.
- Per-process CPU deltas and safe process names.
- Targeted power, thermal, stability, WHEA, bugcheck, and NTFS events.
- Structured `powercfg` battery, energy, and system-power XML.
- Safe service/startup, hotfix, Defender, optional network, and Advanced driver data.

Windows thermal-zone values are best-effort and are never described as guaranteed CPU package/core temperatures.

## Diagnosis

Rules distinguish severity from confidence and avoid treating absent measurements as zero. `INCOMPLETE_DIAGNOSIS` replaces a misleading healthy result when essential event, performance, storage, or thermal evidence is missing.

Correlation combines current load with current thermal readings or recent processor-limit events. Correlated findings state that signals occurred together and do not claim proof of causality. Critical severity is reserved for evidence such as thermal shutdown, WHEA hardware errors, NTFS corruption, and predicted disk failure.

## Trust boundaries

- Collection requires Administrator access because event logs, WMI providers, and power reports may be protected.
- The application executes only fixed Windows binaries with structured argument lists; it does not execute collected content.
- It performs no upload, registry modification, service control, process termination, repair, or power-setting change.
- Public release artifacts require GitHub-hosted build provenance, SignPath signing, Authenticode/timestamp verification, Windows smoke, AWS runtime evidence, and Windows 11 client acceptance.
