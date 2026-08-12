# Capacity planning

SharpAccess supplies bounded package behavior and repeatable reference evidence. Consuming hosts own production sizing, SLOs, traffic modeling, database capacity, alert thresholds, and scaling decisions.

## Enforced package bounds

The current package contract bounds administrative/tenant page size, access-token role/permission counts and encoded size, refresh-family/token counts, password length, concurrent/queued Argon2 work, hash-queue wait, OIDC state/metadata/response sizes, and diagnostic/audit dimensions. Raising a bound requires measured capacity and security review plus directly related tests.

## Reference evidence

`scripts/performance-evidence.ps1` records aggregate duration, allocation, working-set, token-size, Argon2, endpoint, SQLite, and PostgreSQL measurements under `artifacts/performance/release-candidate` using the controlled Windows profile defined by repository policy.

The profile captures explicit warm-up/measurement protocol, sanitized hardware/power information, .NET versions, database/native versions and bounded settings, storage characteristics, and dataset scale. PostgreSQL uses approved scratch configuration without retaining its connection string.

## Baseline approval

The repository supports candidate capture, review, and explicit baseline approval for future release revisions. Approval records the exact revision and controlled environment and is subject to the ancestry/path/tolerance rules in the active policy.

RC1 already completed its required controlled performance evidence. Do not return the repository to a `pending-reference-run` narrative or recapture RC1 performance merely because post-release documentation changed.

For a future selected release, use the current repository commands and review the complete candidate/environment fingerprint before approving. Do not approve on a thermally throttled, heavily contended, power-saving, or otherwise unstable machine.

## Production sizing

Hosts should model login/refresh rate, Argon2 memory times concurrency, connection-pool/database limits, user/tenant/authorization/session/audit volume, backup/restore objectives, credential rotation windows, external identity latency/outage behavior, and multi-instance/proxy/rate-limiter characteristics.

Controlled evidence is comparison evidence, not a production SLA.
