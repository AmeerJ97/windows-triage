# CLI reference

The CLI uses the same collectors, diagnosis rules, privacy model, and report writer as the GUI. Collection requires an elevated Administrator terminal. Informational commands do not require elevation.

## Fast start

```powershell
# Recommended balanced scan
.\WindowsTriage.exe full

# Short scan while the problem is happening
.\WindowsTriage.exe quick --output C:\Temp

# Deeper power and driver evidence
.\WindowsTriage.exe advanced
```

The explicit `collect` form remains supported:

```powershell
.\WindowsTriage.exe collect --profile full
```

## Commands

| Command | Elevation | Behavior |
|---|---:|---|
| `gui` | Yes | Opens the graphical application and requests UAC elevation when needed. |
| `collect` | Yes | Runs collection with `--profile full` unless overridden. |
| `quick` | Yes | Shorthand for a Quick profile collection. |
| `full` | Yes | Shorthand for a Full profile collection. |
| `advanced` | Yes | Shorthand for an Advanced profile collection. |
| `profiles` | No | Explains profile duration, purpose, and examples. |
| `privacy` | No | Explains defaults, opt-ins, and safe sharing. |
| `--help` | No | Prints usage and options. |
| `--version` | No | Prints the executable version. |

## Profiles

| Profile | Default sample | Intended use |
|---|---:|---|
| Quick | 20 seconds | A problem happening now or a fast first pass. |
| Full | 60 seconds | Recommended balanced evidence collection. |
| Advanced | 120 seconds | Deeper power reports and focused driver inventory. |

`--sample-seconds` can override the profile duration from 15 through 900 seconds. `--sample-interval-seconds` accepts 1 through 60 seconds.

## Options

| Option | Description |
|---|---|
| `--profile quick\|full\|advanced` | Select a profile when using `collect`. |
| `--output DIR` | Write the report folder under `DIR`; the desktop is the default. |
| `--no-zip` | Do not create the sibling ZIP archive. |
| `--json` | Write structured diagnostic data to stdout. |
| `--print-summary` | Write the public, redacted summary to stdout. |
| `--open` | Open the report folder after collection. |
| `--quiet` | Suppress progress messages. Report content explicitly requested on stdout is still printed. |
| `--verbose` | Add collector timing and command-status progress without printing sensitive values. |
| `--include-machine-name` | Add the computer name to local reports. |
| `--include-network` | Add network adapter and addressing details. |
| `--include-command-lines` | Add process/service paths and command lines. |
| `--include-private-artifacts` | Retain raw event and power artifacts under `private/`. |

`--json` and `--print-summary` are mutually exclusive because both own stdout. Invalid arguments exit with code `2` and print help.

## Automation examples

```powershell
# Machine-readable output; progress remains on stderr
.\WindowsTriage.exe quick --json --no-zip > diagnostic.json

# Paste-safe Markdown summary
.\WindowsTriage.exe full --quiet --print-summary > public_summary.md

# Keep raw reports for a trusted private investigation
.\WindowsTriage.exe advanced --include-private-artifacts --output C:\PrivateTriage
```

## Output and exit codes

Successful human-readable CLI output prints the full report, JSON, public summary, privacy manifest, and ZIP paths. It also reports the collection-warning count.

| Code | Meaning |
|---:|---|
| `0` | Collection completed without warnings. |
| `1` | Collection completed and reports are usable, but one or more collectors produced warnings. |
| `2` | Invalid command or option. |
| `3` | Collection was started without Administrator privileges or elevation was denied. |
| `4` | Fatal collection or report-generation failure. |

Scripts should accept `0` and `1` as completed collections, but should surface warnings rather than silently treating `1` as fully healthy.
