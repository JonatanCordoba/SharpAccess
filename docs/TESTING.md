# Testing

SharpAccess testing is supported on Windows with PowerShell 7 and .NET 10.

## Test responsibilities

- Unit tests: configuration, validation, cryptography, password security, JWT/OIDC, registration composition, diagnostics, authorization/security boundaries.
- Integration tests: registration, verification, login, lockout, password reset, refresh rotation/replay, administration, tenancy, SQLite recovery.
- Endpoint tests: public HTTP behavior, challenges, authorization, cookies/sessions, password/reset/revocation flows.
- Provider-contract tests: SQLite and PostgreSQL persistence behavior and engine differences.
- Package tests: public surface, identity, provider neutrality/status, Windows-only tooling, workflow security, documentation ownership, and release controls.

## Active providers

| Provider | Status | Validation path |
|---|---|---|
| SQLite | Supported | Always-on contracts; no external DB required |
| PostgreSQL | Supported | Required native/managed real-engine evidence on selected provider/release paths |

SQL Server and MySQL are not active test targets.

PostgreSQL destructive tests require an approved `sharpaccess_contract_tests*` database, `SHARPACCESS_PROVIDER_TEST_ALLOW_RESET=true`, and protected connection configuration.

## Coverage and complexity

`eng/CoveragePolicy.props`, `eng/ProviderCoverage.props`, and `eng/ComplexityPolicy.props` own enforced thresholds and Supported-production scope.

- Core: at least 85% line / 75% branch.
- SQLite: at least 80% line / 65% branch.
- PostgreSQL: at least 80% line / 65% branch as a Supported provider.
- Changed handwritten production code: at least 90% line / 75% branch in the selected scope.
- Supported-production aggregate and complexity/CRAP ratchets include Core, SQLite, and PostgreSQL.

Do not regenerate a baseline merely to accept an unrelated regression.

## Security, mutation, and operations

DevSkim is the blocking SAST implementation. Critical mutation scope includes password/JWT/account-state/tenant isolation/refresh replay/authorization/transaction invariants and PostgreSQL-specific replay/serialization behavior.

```powershell
./scripts/sast.ps1 -RepositoryRoot $PWD
./scripts/mutation-test.ps1 -RepositoryRoot $PWD -Tier Release
./scripts/operational-readiness.ps1 -RepositoryRoot $PWD
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

PostgreSQL continuing aggregate evidence may use the historically named `scripts/postgres-promotion.ps1` with approved scratch configuration.

## Full local verification

Use targeted checks while changes are uncommitted. After committing a coherent change, run the exact clean revision:

```powershell
./scripts/verify-local.ps1 -RepositoryRoot $PWD
git status --short
```

The final status command must be clean. Correct failures, amend the unpushed commit when appropriate, and rerun the complete gate before pushing.

Post-release documentation/control synchronization does not by itself require recapture of immutable RC1 protected provider/OIDC/performance/publication evidence.
