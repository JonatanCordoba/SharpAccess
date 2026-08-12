# Roadmap

## Current implementation status

The authoritative active provider status source is `eng/ProviderStatus.props`; the synchronized package version is owned by `eng/Version.props`.

- `SharpAccess.Core`: **Supported**.
- `SharpAccess.Sqlite`: **Supported**.
- `SharpAccess.Postgres`: **Supported**, with continuing real-engine and release-evidence obligations on applicable future release revisions.

The repository is **Windows-only** for engineering, CI, verification, release, and supported deployment. Automation is **PowerShell 7** only. Bash, Linux/macOS parity, Docker, Compose, service containers, SQL Server implementation, and MySQL implementation are not part of the active tree.

`0.9.0-rc.1` is published. The immutable RC1 package provenance commit is `4595545d8afd84c58795fc02c2c242533cdff1ac`; later post-release `main` commits do not change that provenance. The durable release ledger is `docs/RELEASE-EVIDENCE-MATRIX.md`.

Stable `1.0.0` has not started as an execution stage.

## Status vocabulary

- **Implementation complete and merged**: planned repository changes are present on protected `main`.
- **Evidence complete**: the exact selected revision has the required retained evidence and every referenced approval revision satisfies the applicable identity/ancestry policy.
- **Published**: the immutable release artifacts were successfully published through the protected release flow.
- **Planned**: implementation or release work has not been opened.

A merged pull request or configured workflow does not establish evidence completion by itself. Green branch-head workflows and tree-equivalent commits remain evidence for different revision identities unless the applicable policy explicitly permits reuse.

## `0.9.0-rc.1`

RC1 implementation, protected release evidence, signed tag, NuGet Trusted Publishing, public package/symbol publication, clean-consumer validation, GitHub prerelease, and live Wiki publication are complete.

Post-release documentation, Wiki, governance, branch-cleanup, and legacy-retirement work may advance `main` without requiring RC1 PostgreSQL, OIDC, controlled performance, package, or publication evidence to be recaptured.

The active RC1/migration closure execution roadmap is maintained in the project Master Prompt until that stage is closed; it is intentionally not duplicated here.

## Future provider roadmap

SQL Server and MySQL remain **future roadmap candidates** only. They are not active projects, dependencies, package IDs, namespaces, registrations, workflows, scripts, tests, operational runbooks, or release blockers.

A future proposal to introduce either provider requires a new accepted ADR and separately reviewed plan covering provider-neutral compatibility, native Windows database support, migrations and historical upgrades, transactions/concurrency, error classification, restricted principals, query plans, coverage/mutation evidence, backup/recovery, package consumers, security/operations, and release/rollback policy.

No placeholder source or dormant dependency may be added before such a proposal is accepted.

## Other future work

Redis, passkeys/WebAuthn, additional identity-provider presets, distributed rate limiting implementations, additional databases, vendor-specific managed-key packages, and optional observability exporters remain uncommitted ideas unless separately designed, implemented, tested, documented, and approved.
