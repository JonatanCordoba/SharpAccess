# Testing and Verification

SharpAccess uses five test projects plus revision-bound PowerShell orchestration.

## Test ownership

| Project | Responsibility |
|---|---|
| `SharpAccess.UnitTests` | Core behavior and provider-neutral contracts. |
| `SharpAccess.IntegrationTests` | Application flows against SQLite. |
| `SharpAccess.EndpointTests` | HTTP behavior, policies, smoke, and bounded endpoint-performance evidence. |
| `SharpAccess.ProviderContractTests` | Registration, options, migrations, persistence security, transactions, concurrency, SQLite, and PostgreSQL contracts. |
| `SharpAccess.PackageTests` | Package surface, public API, identity, topology, documentation, versioning, and repository policy. |

## Full local verification

```powershell
./scripts/verify-local.ps1 -RepositoryRoot $PWD -Version '0.9.0-rc.1'
```

The command is intentionally clean-tree and revision-bound.

## Supported-provider evidence

PostgreSQL execution can be optional during ordinary unconfigured development, but it is mandatory for Supported-provider and release evidence. SQLite-only success is not PostgreSQL evidence.

## Public hosted check identities

The current disambiguated workflow job display names include:

- `ci-windows`;
- `operational-readiness-windows`;
- `provider-contracts-classify`;
- `test-scope-classify`.

Other release-relevant jobs retain their own names, including `sqlite-supported`, `postgres-native`, `devskim`, `tracked-secret-scan`, `review`, `Validate pull request evidence`, and `windows-release-candidate`.

Only a completed successful conclusion is passing evidence. Skipped, cancelled, timed-out, neutral, action-required, and infrastructure-blocked conclusions must remain accurately classified. Required-check configuration must use the exact names observed on successful runs from the protected revision; a tracked manifest must be synchronized separately before it is treated as authoritative.

## Quality and security

Verification includes coverage, changed-line coverage, complexity/CRAP ratchets, critical mutation invariants, SAST, dependency audit, secret scanning, package tests, SBOMs, and repository-structure policy.

## References

- [Testing](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/TESTING.md)
- [Quality gates](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/QUALITY-GATES.md)
- [Provider contract testing](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/PROVIDER-CONTRACT-TESTING.md)
- [Quality and Metrics](Quality-and-Metrics)
