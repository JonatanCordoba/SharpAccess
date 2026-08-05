# Capacity planning

SharpAccess supplies bounded package behavior and repeatable reference evidence. The consuming host owns production sizing, service-level objectives, traffic modeling, database capacity, alert thresholds, and scaling decisions.

## Enforced package bounds

The stable contract enforces:

| Resource | Default or maximum |
|---|---:|
| Administrative and tenant page size | maximum 200 |
| Roles encoded in one access token | maximum 32 |
| Permissions encoded in one access token | maximum 128 |
| Encoded access-token size | maximum 8,192 bytes |
| Active refresh families per user | maximum 10 |
| Active refresh tokens per family | maximum 20 |
| Password length | maximum 256 characters |
| Concurrent Argon2 operations | bounded by `MaximumConcurrentPasswordHashes` |
| Queued Argon2 operations | bounded by `MaximumQueuedPasswordHashes` |
| Password-hash queue wait | bounded by `PasswordHashQueueTimeout` |
| OIDC return URL, state, metadata, and response sizes | bounded by validated options and request contracts |
| Audit detail and diagnostics dimensions | bounded and secret-free |

These limits are security and resource-safety controls. Raising one requires a measured capacity review, security review, and directly related tests.

## Reference evidence

Run:

```powershell
./scripts/performance-evidence.ps1 -RepositoryRoot $PWD -ReferenceEnvironment "<stable-label>"
```

The script records aggregate durations, allocations, working-set deltas, token size, configured Argon2 memory, concurrency limits, and endpoint measurements under `artifacts/performance/release-candidate`.

Measured operations include:

- password hashing and verification;
- bounded password-hash queue saturation and no-wait rejection;
- JWT signing and strict validation;
- authorization-context construction;
- login and persisted account-state validation;
- refresh-token rotation and concurrent replay contention;
- bounded SQLite user and tenant-member pagination;
- bounded PostgreSQL user and tenant-member keyset pagination;
- global-role assignment and removal with session invalidation.

The controlled profile retains explicit warm-up counts, repeated measured iterations, sanitized Windows hardware and power-plan metadata, .NET SDK/runtime versions, SQLite/native versions, PostgreSQL server and bounded configuration values, storage characteristics, and dataset scale. PostgreSQL uses the existing opt-in scratch database and never retains its connection string.

Each controlled capture runs the complete metric catalog in three independent test processes. For every metric, the retained p95, allocation, and supporting aggregate fields are the median across those processes. The individual p95 and allocation observations remain in the evidence for review. A single transient slow process therefore cannot approve or reject a revision, while degradation in at least two of three processes remains blocking.

## Baseline approval

`eng/PerformanceBaseline.json` begins in `pending-reference-run` state. The first controlled run generates `candidate-baseline.json` in the artifacts directory. Review the environment fingerprint, hardware and power plan, runtimes, database versions and bounded settings, dataset scale, warm-up counts, p95 values, allocation values, and unexpected outliers.

Approve only through the repository-supported path:

```powershell
./scripts/performance-evidence.ps1 `
  -RepositoryRoot $PWD `
  -Configuration Release `
  -ReferenceEnvironment 'controlled-windows-runner-01' `
  -ApproveBaseline `
  -ReviewDecision '<reviewed decision>'
```

The approval command promotes the existing reviewed aggregate candidate without rerunning measurements. It also writes the revision and environment fingerprint using JSON-equivalent Unicode escapes so repository SAST does not mistake evidence hashes for secrets.

Commit only `eng/PerformanceBaseline.json` after approval. The approved revision may be followed only by that metadata-only baseline commit; any later source, test, script, policy, or documentation change invalidates approval. Then run with `-RequireApprovedBaseline`. The ratchet rejects an incomplete environment, missing metrics, a changed environment fingerprint, a changed independent-run protocol, invalid revision scope, or median p95/allocation growth beyond the tolerance in `eng/ReleaseCandidate.props`.

Do not approve a baseline captured on a thermally throttled, heavily contended, power-saving, or otherwise unstable machine.

## Production sizing

A host should model at least:

- expected login and refresh rate;
- Argon2 memory multiplied by concurrent hashing operations;
- connection-pool size and database connection limits;
- active users, tenants, memberships, roles, permissions, refresh families, and audit volume;
- audit retention and export volume;
- backup duration, restore duration, and recovery-point objective;
- key and credential rotation windows;
- external identity-provider latency and outage behavior;
- proxy, rate-limiter, and multi-instance deployment behavior.

Local and GitHub-hosted numbers are comparison evidence, not a production SLA. Validate the final deployment with representative data, network latency, database topology, encryption, observability, and failure injection.
