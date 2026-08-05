# SharpAccess.Postgres

`SharpAccess.Postgres` is the supported PostgreSQL persistence provider for SharpAccess.

## Status

**Supported.** The project participates in the supported package path and exposes the reviewed public `AddPostgresAccess` registration surface. The promotion branch must not merge without exact-revision provider evidence.

## Validation

Use a dedicated scratch database named `sharpaccess_contract_tests` or beginning with `sharpaccess_contract_tests_`.

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = "<scratch PostgreSQL connection string>"
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = "true"
$env:SHARPACCESS_POSTGRES_READINESS = "true"

./scripts/postgres-promotion.ps1 -RepositoryRoot $PWD
```

The tests reset provider-owned `auth_*` tables. The shared guard rejects a differently named database or a missing reset acknowledgment before destructive SQL runs. See `docs/PROVIDER-CONTRACT-TESTING.md`, `docs/PROVIDER-STATUS.md`, and `docs/POSTGRES-PROMOTION.md`.

## Source organization

Provider code is organized under `Configuration`, `DependencyInjection`, `Persistence`, `Migrations`, `Stores`, and `Internal`. Only the reviewed options and registration surface are public; SQL, migrations, stores, connection factories, dialects, transaction managers, and classifiers remain internal.