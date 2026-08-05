# Provider status

`eng/ProviderStatus.props` is the authoritative active package-status source. Project packability, public registration, scripts, workflows, tests, documentation, SBOM roots, and release evidence must agree with it.

## Active status

| Project or package | Status | Public host registration | Ordinary package artifact |
|---|---|---|---|
| `SharpAccess.Core` | Supported | `AddSharpAccess` | Yes |
| `SharpAccess.Sqlite` | Supported | `AddSqliteAccess` | Yes |
| `SharpAccess.Postgres` | Supported | `AddPostgresAccess` | Yes |

## Continuing PostgreSQL support evidence

PostgreSQL support remains valid only while applicable revisions continue to pass:

- native or approved managed real-engine provider contracts;
- readiness and restricted-principal evidence;
- schema creation and historical migration upgrades;
- transaction, concurrency, cancellation, timeout, and SQLSTATE behavior;
- bounded-query and native query-plan evidence;
- provider coverage and PostgreSQL-specific mutation evidence;
- native logical backup/restore evidence;
- package validation and package-consumer smoke tests.

`scripts/postgres-promotion.ps1` retains its historical name but is also the canonical aggregate command for continuing Supported-provider evidence. A passing SQLite-only gate is not PostgreSQL release evidence, and prior promotion evidence does not permanently verify later changed revisions.

Protected OIDC, the approved controlled performance baseline, canonical export, publication, and post-publication verification remain independent release-candidate and stable-release gates.

## Future providers

SQL Server and MySQL appear only in `docs/ROADMAP.md` as future candidates. Their previous implementation projects, dependencies, scripts, workflows, tests, runbooks, public API baselines, and package metadata are not active repository content.

Reintroduction requires a new accepted ADR and an independent implementation and promotion gate. Roadmap awareness is not a compatibility or delivery commitment.
