# ADR 0020: Keep only Core, SQLite, and PostgreSQL in the active repository cohort

## Status

Accepted on 2026-07-25.

## Context

The repository previously contained implementation, tests, dependencies, scripts, CI jobs, recovery tooling, and documentation for SQLite, PostgreSQL, SQL Server, and MySQL. SQL Server and MySQL were not part of the initial stable package cohort and carried substantial inactive maintenance and evidence obligations.

The initial stable release requires a focused, reviewable source tree. Core and SQLite are already supported. PostgreSQL remains the required initial server-provider promotion target.

## Decision

The active repository cohort is:

- `SharpAccess.Core` — Supported;
- `SharpAccess.Sqlite` — Supported;
- `SharpAccess.Postgres` — Internal implementation in progress until promotion.

SQL Server and MySQL are removed from the active solution, projects, dependencies, namespaces, public API baselines, scripts, workflows, tests, runbooks, and package surface.

They remain future roadmap candidates only. Reintroducing either provider requires a new accepted ADR and a separately reviewed implementation and promotion plan.

## Consequences

Positive:

- smaller release tree and dependency graph;
- lower code, test, documentation, and operational complexity;
- clearer stable-release boundary;
- no dormant provider implementation presented as maintained.

Trade-offs:

- prior SQL Server and MySQL implementation is available only in private development history;
- users receive no compatibility commitment for those providers;
- future reintroduction must repeat security, migration, recovery, package, and release review.

## Reintroduction requirements

A future SQL Server or MySQL proposal must define and retain evidence for:

- provider-neutral API compatibility;
- native Windows database operation;
- schema creation and historical upgrades;
- transaction, isolation, concurrency, and error semantics;
- restricted-principal operation;
- bounded queries and query plans;
- provider coverage and mutation evidence;
- backup and recovery;
- package-consumer validation;
- security and operational documentation;
- synchronized release, rollback, and support policy.

No placeholder project, dependency, registration method, package ID, or workflow may be added before that proposal is accepted.
