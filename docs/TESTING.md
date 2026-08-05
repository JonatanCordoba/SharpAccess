# Testing

SharpAccess testing is supported on Windows with PowerShell 7 and .NET 10.

## Test responsibilities

- Unit tests cover configuration, validation, cryptography, password security, JWT behavior, OIDC, registration composition, diagnostics, and security boundaries.
- Integration tests cover registration, verification, login, lockout, password reset, refresh rotation, replay detection, administration, tenancy, and SQLite recovery.
- Endpoint tests cover the static console, health, malformed JSON, challenges, administration authorization, cookies, sessions, password reset, and revocation.
- Provider-contract tests cover SQLite and PostgreSQL persistence behavior and engine differences.
- Package tests cover public surface, identity, provider neutrality, active status, Windows-only tooling, workflow security, documentation ownership, and release controls.

## Active provider validation

| Provider | Status | Validation path | External database required |
|---|---|---|---|
| SQLite | Supported | Always-on provider contracts | No |
| PostgreSQL | Supported | Required native/managed real-engine promotion and release evidence | Yes for promotion and release gates |

SQL Server and MySQL are not active test targets.

## Destructive PostgreSQL safety

PostgreSQL tests reset provider-owned `auth_*` objects. They run only when:

- the database is `sharpaccess_contract_tests` or begins with `sharpaccess_contract_tests_`;
- `SHARPACCESS_PROVIDER_TEST_ALLOW_RESET=true`;
- the approved connection string is supplied outside source control.

## Coverage

`eng/CoveragePolicy.props` defines supported and changed-code gates. `eng/ProviderCoverage.props` defines SQLite and PostgreSQL provider thresholds.

- Core: minimum 85% line and 75% branch.
- SQLite: minimum 80% line and 65% branch.
- Changed handwritten production code: minimum 90% line and 75% branch in the selected scope.
- PostgreSQL promotion: reviewed 80% line and 65% branch thresholds.

Provider attribution uses `Provider` traits. Coverage evidence includes provider contracts, infrastructure, registration, migrations, transactions, error classification, and provider-specific regressions.

## Complexity

Every active production assembly belongs to one scope in `eng/ComplexityPolicy.props`:

- Core and SQLite: approved-baseline ratchet.
- PostgreSQL: report-only until a promotion ratchet is reviewed.

Do not regenerate a baseline to accept an unrelated regression.

## Security and mutation

Microsoft DevSkim is the blocking SAST implementation. SARIF evidence is retained under `artifacts/sast`.

```powershell
./scripts/sast.ps1 -RepositoryRoot $PWD
```

Critical mutation cases cover password verification, JWT key selection, account state, tenant isolation, refresh rotation/replay, authorization fail-closed behavior, transaction rollback, PostgreSQL refresh replay, and PostgreSQL serialization-failure classification.

```powershell
./scripts/mutation-test.ps1 -RepositoryRoot $PWD -Tier Release
```

## Operational tests

```powershell
./scripts/operational-readiness.ps1 -RepositoryRoot $PWD
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

Generated records are ignored under `artifacts/operations`.

## Provider commands

SQLite:

```powershell
./scripts/sqlite-smoke.ps1 -RepositoryRoot $PWD
```

PostgreSQL:

```powershell
$env:SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING = '<approved scratch database connection string>'
$env:SHARPACCESS_PROVIDER_TEST_ALLOW_RESET = 'true'
$env:SHARPACCESS_POSTGRES_READINESS = 'true'

./scripts/postgres-promotion.ps1 -RepositoryRoot $PWD
```

## Repository structure

```powershell
./scripts/verify-structure.ps1 -RepositoryRoot $PWD
```

The structure gate enforces:

- Windows-only workflows;
- PowerShell 7 scripts with strict mode;
- no Bash or container topology;
- exactly SQLite and PostgreSQL provider projects;
- no SQL Server/MySQL active surface;
- lock-file ownership;
- solution/project agreement;
- authoritative provider status and roadmap awareness.

## Full local verification

Run targeted checks while changes are uncommitted. After committing, run the exact clean revision:

```powershell
./scripts/verify-local.ps1 -RepositoryRoot $PWD
git status --short
```

The final status command must produce no tracked or untracked output. Correct failures, amend the unpushed commit, and rerun the complete gate before pushing.