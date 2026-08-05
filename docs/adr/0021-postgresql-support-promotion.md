# ADR 0021: Promote PostgreSQL through one exact-revision provider-specific evidence gate

- Status: Accepted
- Decision date: 2026-07-27

## Context

SharpAccess 1.0 uses Core, SQLite, and PostgreSQL as its active package cohort. PostgreSQL implementation, migrations, real-engine contracts, native recovery tooling, and operational documentation already exist, but the provider remained internal while promotion evidence and package/public-surface controls were incomplete.

Provider support must not be inferred from implementation presence, a build, SQLite verification, configured workflows, or an uncommitted tree. Protected OIDC and the controlled performance baseline are release-level controls and are not provider-specific prerequisites for classifying PostgreSQL as Supported.

## Decision

Promote `SharpAccess.Postgres` in one coherent revision that:

- changes `eng/ProviderStatus.props` to Supported;
- exposes `Microsoft.Extensions.DependencyInjection.PostgresServiceCollectionExtensions`;
- records the registration type in the public API baseline;
- includes PostgreSQL in supported package creation, package smoke, formal SBOM archive requirements, and release checks;
- requires real-engine contracts, restricted principals, empty and historical migrations, concurrency, cancellation, timeout, SQLSTATE mapping, bounded queries, query plans, promotion coverage, PostgreSQL-specific mutations, and native recovery;
- writes revision-bound redacted promotion evidence;
- leaves protected OIDC, controlled performance, canonical export, publication, and post-publication checks as separate stable-release blockers.

The complete local release gate requires PostgreSQL configuration after this revision because PostgreSQL is no longer an incubation-only provider.

## Consequences

### Security and data integrity

Refresh replay and SQLSTATE classification receive PostgreSQL-specific mutation coverage. Recovery uses approved scratch databases and native PostgreSQL tools. Credentials and connection strings are never retained in promotion evidence.

### Public API and compatibility

`AddPostgresAccess` becomes public before stable freeze. Obsolete internal promotion aliases are removed rather than preserved. Once a stable package is published, ordinary public API compatibility policy applies.

### Packaging and supply chain

Core, SQLite, and PostgreSQL share the synchronized version and supported package catalog. Package smoke compiles both supported provider registrations. Formal release-candidate SBOM generation requires archives for all supported packages.

### Operations

The promotion gate requires Windows, PowerShell 7, the repository .NET SDK, Git, an approved resettable PostgreSQL database, and native PostgreSQL client tools.

### Rollback

Before publication, revert the coherent promotion commit set. Do not revert only status or only public API because partial rollback would create a misleading package and evidence state.