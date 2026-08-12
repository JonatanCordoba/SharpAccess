# Project manifest

## Runtime, platform, repository, and release state

- Target framework: `net10.0`.
- SDK: the exact version selected by `global.json`.
- Supported engineering, CI, verification, release, and deployment platform: Windows.
- Repository automation: PowerShell 7 only.
- Container policy: no Dockerfiles, Compose files, service containers, or local container orchestration.
- License: MIT. Canonical package license metadata is `<PackageLicenseExpression>MIT</PackageLicenseExpression>` in `Directory.Build.props`.
- Canonical source, development, review, release, and publication repository: `JonatanCordoba/SharpAccess`.
- Canonical protected source branch: `main`.
- Authoritative synchronized package version: `eng/Version.props`.
- Published prerelease version: `0.9.0-rc.1`.
- Published signed tag: `v0.9.0-rc.1`.
- Immutable RC1 package provenance commit: `4595545d8afd84c58795fc02c2c242533cdff1ac`.
- Current post-release `main` at the audited closure baseline: `a038e7fc364c526f956ebacce7f5779fbfaac0a8`.

Current `main` is intentionally newer than the immutable RC1 provenance commit. Documentation, Wiki, governance, and stage-closure commits after publication do not change the source identity of the already-published RC1 package bytes.

Stable `1.0.0` has not started as an execution stage.

## Active package cohort

- `SharpAccess.Core`: Supported.
- `SharpAccess.Sqlite`: Supported.
- `SharpAccess.Postgres`: Supported.
- Provider status source of truth: `eng/ProviderStatus.props`.

PostgreSQL remains subject to continuing real-engine contracts, restricted-principal, historical-upgrade, concurrency, cancellation, timeout/SQLSTATE, query-plan, coverage, mutation, native-recovery, package-validation, and consumer-evidence obligations on applicable future release revisions.

SQL Server and MySQL are absent from the active repository tree. They remain future roadmap candidates only and may return only through separate architecture, implementation, security, compatibility, migration, operations, and release-evidence work.

## Project layout

- `src/SharpAccess.Core`: provider-neutral package source.
- `providers/SharpAccess.Sqlite`: supported SQLite provider source.
- `providers/SharpAccess.Postgres`: supported PostgreSQL provider source.
- `providers/Shared`: linked internal registration source shared by active providers.
- `samples/SharpAccess.SampleApi`: Minimal API sample and test-console host.
- `tools/SharpAccess.TestBootstrap`: deterministic test bootstrap.
- `tools/SharpAccess.MigrationTool`: migration command-line utility.
- `tools/SharpAccess.Sbom`: deterministic active-cohort SBOM generator.
- `tools/SharpAccess.QualityReport`: exact-revision engineering-quality report generator.
- `tests/*`: unit, integration, endpoint, provider-contract, and package/repository-policy tests.
- `scripts`: PowerShell 7 verification, quality, provider, recovery, and release tools.
- `.github/workflows`: Windows-only GitHub Actions workflows.
- `docs`: consumer, operator, security, quality, governance, and release documentation.
- `wiki-source`: tracked source for the separately published GitHub Wiki.

Every active project inherits lock-file generation from `Directory.Build.props`. Project-level lock-file opt-outs are forbidden.

## Publication policy

The RC1 cohort consists exactly of Core, SQLite, and PostgreSQL. RC1 publication is complete and was performed through `.github/workflows/publish-nuget.yml` using NuGet Trusted Publishing with GitHub OIDC and the protected publication boundary. The publication workflow consumed the exact validated release-candidate artifact; it did not rebuild or repack the release cohort.

The durable immutable RC1 identity ledger is `docs/RELEASE-EVIDENCE-MATRIX.md`.

The SharpAccess Git history began from a signed, history-clean migration root. That bootstrap is historical evidence, not an instruction to recreate or replace the existing repository. See `docs/RELEASE-REPOSITORY-BOOTSTRAP.md`.

## Provider-test safety

PostgreSQL contract and recovery tests require an approved scratch database, explicit reset acknowledgment, protected connection configuration, and native Windows PostgreSQL tools where recovery evidence is selected. Missing required PostgreSQL infrastructure fails a selected release/provider evidence path rather than being silently treated as success.
