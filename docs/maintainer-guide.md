# Maintainer guide

This guide explains the community automation and the human controls around it.

## Labels

- `area/*` labels identify the affected product surface.
- `type/*` labels communicate release-note intent.
- `status/*` labels communicate workflow state.
- `priority/*` labels are human triage decisions and are never assigned automatically.
- Existing `bug`, `enhancement`, `documentation`, `dependencies`, `good first issue`, and `help wanted` labels remain supported.

Path labeler synchronizes only `area/*`, `dependencies`, and `platform/windows`
labels on pull requests. Issue forms assign their own initial labels. Maintainers
remain responsible for severity, priority, validity, and release-impact labels.

## Automation

| Workflow | Purpose | Write access | Explicit limit |
|---|---|---|---|
| `welcome` | Greet a contributor's first issue or PR. | Issues and PRs | No assignment or closure. |
| `pull-request-labeler` | Apply path-based labels. | PR labels | Never checks out fork code. |
| `dependency-review` | Reject newly introduced high-severity vulnerable dependencies. | Failure comment only | Does not update dependencies. |
| `stale-triage` | Mark inactive issues/PRs for review. | Labels/comments | Never auto-closes. |
| `release-drafter` | Maintain a human-reviewed draft release. | Draft release | Never publishes. |
| `Dependabot Updates` | Propose dependency and action updates. | Pull requests | Never auto-merges. |

All third-party actions are pinned to immutable commits and workflows declare
minimal permissions. Review Dependabot action updates before accepting a new
commit SHA.

## Triage flow

1. Confirm the report contains no sensitive diagnostic bundle.
2. Reproduce or classify the request.
3. Apply area, type, and status labels.
4. Ask for the minimum additional safe evidence.
5. Link duplicates instead of splitting discussion.
6. Reserve `good first issue` for tasks with bounded scope and acceptance checks.
7. Close only with a clear resolution or rationale; stale automation never closes.

## Pull requests

Require a clear user outcome, privacy impact, tests or evidence, documentation
updates when behavior changes, and green CI/Windows/CodeQL checks. Do not merge
generated dependency updates solely because a bot opened them.

## Releases

Release Drafter prepares notes from merged PR labels. The signed release
workflow remains manual and requires external Windows 11 evidence, AWS evidence,
SignPath configuration, signature verification, and a post-signing checksum.
Update the explicit name and tag in `.github/release-drafter.yml` when starting
a new release cycle; bots never choose or publish the final version.
