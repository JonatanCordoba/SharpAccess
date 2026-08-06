# Performance and Capacity

SharpAccess performance evidence is revision-bound comparison evidence, not a production SLA. Hosts must validate representative hardware, DB topology, data volume, encryption, network latency, and failure conditions.

## Measured operations

The controlled Windows evidence catalog includes:

- password hashing and verification;
- bounded password-hash queue saturation and no-wait rejection;
- JWT signing and strict validation;
- authorization-context construction;
- login and persisted account-state validation;
- refresh rotation and concurrent replay contention;
- SQLite and PostgreSQL bounded keyset pagination;
- global-role assignment/removal with session invalidation.

## Evidence protocol

Each controlled capture:

- runs on a documented Windows reference environment;
- records the exact revision and tree;
- uses warmups and repeated measured iterations;
- runs the complete metric catalog in three independent processes;
- retains p50, p95, maximum duration, allocations, and supporting metadata;
- aggregates retained p95 and allocation values as the median across independent processes;
- excludes credentials, raw tokens, identifiers, connection strings, SQL, and exception messages.

## Enforced resource bounds

| Resource | Default or maximum |
|---|---:|
| Administrative and tenant page size | 200 |
| Roles in one access token | 32 |
| Permissions in one access token | 128 |
| Encoded access-token size | 8,192 bytes |
| Active refresh families per user | 10 |
| Active refresh tokens per family | 20 |
| Password length | 256 characters |
| Concurrent Argon2 work | configured bound |
| Queued Argon2 work | configured bound |
| Hash-queue wait | configured timeout |

## Candidate capture

```powershell
./scripts/performance-evidence.ps1 `
    -RepositoryRoot $PWD `
    -Configuration Release `
    -ReferenceEnvironment 'local-controlled'
```

A candidate must be reviewed before approval. Approval promotes the existing reviewed candidate without rerunning measurements.

## Baseline validity

The approved revision must be the selected revision or an ancestor whose later changes are limited by policy. Tree equivalence is not enough. A squash merge can invalidate branch-head evidence because the approved commit is no longer an ancestor.

## Production sizing

Model login/refresh rate, Argon2 memory times concurrency, pool size, DB limits, users/tenants/roles, audit volume, backup/restore duration, key rotation, identity-provider latency, proxy behavior, and multi-instance topology.

## References

- [Performance contract](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/PERFORMANCE.md)
- [Capacity planning](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/CAPACITY-PLANNING.md)
- [Quality and Metrics](Quality-and-Metrics)
