# PostgreSQL support promotion

`eng/ProviderStatus.props` is the authoritative provider-status source. `SharpAccess.Postgres` is exposed and packed as Supported only in the coordinated promotion revision.

## Scope

The promotion revision changes one provider boundary:

- `AddPostgresAccess` becomes public;
- the reviewed public API baseline includes the PostgreSQL registration type;
- the package becomes part of the ordinary supported package catalog;
- package smoke consumes the PostgreSQL package and compiles its public registration;
- release SBOM requirements include Core, SQLite, and PostgreSQL;
- PostgreSQL-specific promotion mutations are mandatory;
- the complete clean-tree gate fails when required PostgreSQL evidence is not configured.

It does not complete protected OIDC evidence, approve the controlled performance baseline, transition canonical repository metadata, export the clean public root, or publish `1.0.0`.

## Required environment

Use only an approved resettable scratch database. Never paste the connection string into logs, documentation, commits, branch notes, or retained evidence.

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
$env:SHARPACCESS_POSTGRES_READINESS = 'true'
```

Windows must provide PowerShell 7, the .NET SDK selected by `global.json`, Git, and native PostgreSQL tools:

- `psql`
- `createdb`
- `dropdb`
- `pg_dump`
- `pg_restore`

## Exact-revision gate

Fetch the current base branch, then run against a committed clean tree because `verify-local` is intentionally clean-tree and revision-bound.

```powershell
git fetch origin

./scripts/postgres-promotion.ps1 `
  -RepositoryRoot $PWD `
  -Configuration Release `
  -ChangedCodeBaseRef origin/master
```

The script runs:

1. real-engine provider contracts and readiness;
2. empty and historical migration paths;
3. restricted-principal, transaction, isolation, concurrency, cancellation, timeout, SQLSTATE, bounded-query, and query-plan evidence;
4. promotion line, branch, and changed-code coverage across the complete promotion branch;
5. PostgreSQL-specific mutation evidence;
6. native `pg_dump`/`pg_restore` recovery;
7. the complete clean-tree Windows gate;
8. package creation, package validation, package-consumer compilation, and SBOM generation.

The final summary is written to `artifacts/postgres-promotion/evidence.json`. The summary contains hashes and paths, not credentials or connection strings.

## Pull-request or branch acceptance

Do not merge the promotion revision unless:

- the evidence revision equals the reviewed branch head;
- every required PostgreSQL stage passed;
- the working tree remained clean;
- `SharpAccess.Postgres` runtime and symbol packages exist at the synchronized version;
- the package consumer used package references rather than project references;
- the public API baseline and provider status agree;
- no retained artifact contains credentials, connection strings, raw tokens, or production personal data.

GitHub-hosted infrastructure failure is not passing evidence. Local exact-revision evidence may be used for review, but hosted required checks remain separately enforceable when service quota is available.

## Rollback

Before stable publication, rollback is a revert of the coherent promotion commit set. The revert must restore provider status, package catalog membership, public API baseline, public registration visibility, package-consumer expectations, release SBOM requirements, mutation catalog entries, and promotion documentation together.

After any stable package is published, public API and package compatibility policy applies. Do not silently make the provider internal or unpublish a released version.