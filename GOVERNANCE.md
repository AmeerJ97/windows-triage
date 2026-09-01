# Governance

Windows Triage is an owner-maintained open-source project. The current project
maintainer and final decision maker is [AmeerJ97](https://github.com/AmeerJ97).

## Decision process

- Small fixes and documentation changes can proceed through a focused pull request.
- User-visible features should begin with an issue or Discussion describing the problem and privacy implications.
- Security-sensitive changes require explicit threat and privacy review.
- Architecture changes should preserve the read-only, local-only product boundary unless the community accepts a documented proposal.
- The maintainer resolves final scope, release, compatibility, and safety decisions.

Consensus is preferred, but silence does not imply approval. Roadmap entries are
intentions rather than promises, and availability is best-effort.

## Maintainer responsibilities

- keep contribution and security guidance current;
- review changes for correctness, privacy, and maintainability;
- keep supported dependencies and CI actions serviced;
- publish only artifacts that pass documented release gates;
- communicate breaking changes and known limitations honestly;
- apply the Code of Conduct consistently.

## Becoming a maintainer

Additional maintainers may be invited after sustained, high-quality
contributions and demonstrated judgment around Windows behavior, privacy, and
community support. Repository access is never granted automatically by a bot or
contribution count.

## Automation authority

Repository bots may label, welcome, summarize dependency risk, mark inactivity,
and maintain a draft release. They may not merge pull requests, close stale
conversations, publish releases, change branch protection, or bypass human
release approval.

