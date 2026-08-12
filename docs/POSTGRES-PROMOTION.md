# PostgreSQL support promotion — historical record and continuing aggregate evidence

## Status

`SharpAccess.Postgres` is already **Supported**. `eng/ProviderStatus.props` is the authoritative current status source. ADR 0021 and this document retain the historical promotion decision/procedure so the reason for the public/provider boundary remains reviewable.

The historical promotion established the public `AddPostgresAccess` registration, reviewed public API baseline, ordinary supported package membership, package-consumer validation, SBOM membership, PostgreSQL-specific mutations, real-engine contracts, coverage, restricted-principal, query-plan, and native recovery requirements.

Promotion is not pending and must not be rerun merely to prove that PostgreSQL is currently Supported.

## Continuing evidence command

The retained script name is historical. On a future applicable exact revision it can still aggregate continuing PostgreSQL evidence:

```powershell
./scripts/postgres-promotion.ps1 `
  -RepositoryRoot $PWD `
  -Configuration Release `
  -ChangedCodeBaseRef origin/main
```

Use only an approved resettable scratch database and never retain the connection string:

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
$env:SHARPACCESS_POSTGRES_READINESS = 'true'
```

Native recovery evidence requires `psql`, `createdb`, `dropdb`, `pg_dump`, and `pg_restore` on Windows.

The aggregate command covers real-engine provider contracts/readiness, historical migrations, restricted principals, transactions/concurrency/cancellation/timeouts/SQLSTATE, bounded queries/query plans, provider coverage, PostgreSQL-specific mutations, native recovery, clean-tree verification, package validation/consumer compilation, and SBOM evidence as implemented by the current script.

## Evidence interpretation

The output is exact-revision evidence. Missing required PostgreSQL infrastructure fails a selected required path rather than being treated as a pass. Retained evidence must not contain credentials, connection strings, raw tokens, or production personal data.

Protected OIDC, controlled performance, tagging, and publication are separate release concerns. RC1 already completed those concerns for its immutable release revision; post-release documentation changes do not recapture them.

## Future change boundary

A future change that materially affects PostgreSQL must continue to satisfy the applicable Supported-provider evidence. Removing or deferring PostgreSQL would require a separately reviewed compatibility/status decision; it is not accomplished by editing this historical promotion record.
