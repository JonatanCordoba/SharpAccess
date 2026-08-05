# ADR 0015: Release Core, SQLite, and PostgreSQL as the initial stable cohort

## Status

Accepted on 2026-07-22. Supersedes ADR 0002 and ADR 0003 for stable-release scope and provider-promotion sequencing.

## Context

SharpAccess contains one provider-neutral core package and four relational provider projects. Requiring SQL Server and MySQL to reach full promotion evidence before the first stable release makes the supported Core, SQLite, and PostgreSQL path depend on later provider work. The package family still needs synchronized versions for every package included in a public stable release and truthful incubation boundaries for providers that are not yet promoted.

## Decision

The initial stable `1.0.0` release cohort is:

- `SharpAccess.Core`
- `SharpAccess.Sqlite`
- `SharpAccess.Postgres`

PostgreSQL remains Internal implementation in progress until its complete initial-release promotion gate passes. SQL Server and MySQL remain Internal implementation in progress through the initial stable release and do not block it while their registration surfaces remain internal and their packages remain unavailable as stable artifacts.

A later SQL Server or MySQL promotion joins the then-current synchronized package-family release only after that provider independently satisfies the full real-engine, migration, recovery, package-consumer, security, coverage, documentation, and protected-publication gates.

## Consequences

- SQLite remains the supported zero-infrastructure reference provider.
- PostgreSQL is the initial stable server-provider target.
- SQL Server and MySQL retain implementation and scheduled validation without creating an initial-release dependency.
- Public stable packages included in the same release use one synchronized version.
- Later provider promotion does not retroactively publish a misleading provider-specific `1.0.0`.

## Guardrails

- `eng/ProviderStatus.props` remains the current support-status source of truth.
- No incubation provider exposes public registration or stable package output.
- The stable-publication SBOM archive gate requires exactly the packages in the approved release cohort.
- Documentation and release evidence distinguish initial-release requirements from later-promotion blockers.
