# Performance contract

SharpAccess bounds collection and security-resource work by contract rather than caller convention.

## Cursor pagination

Users, audit records, global roles, global permissions, caller tenants, and tenant members use keyset pagination.

- Core rejects limits outside 1–200 before persistence.
- Providers defensively enforce the same range.
- Queries fetch at most 201 logical keys and emit at most 200 items.
- Continuation uses the final emitted creation-time/identifier pair.
- Separate first-page and continuation SQL preserves seekable predicates.
- Migration `012_pagination_indexes` supplies matching indexes for SQLite and PostgreSQL.

Tenant-member projection selects the bounded membership keyset before joining role rows, so multiple roles do not duplicate logical page items.

## Consistency model

Pagination is a forward traversal, not a repeatable-read snapshot. Newer inserts do not appear behind an existing boundary. Deletions and authorization changes between requests may change later visibility.

## Security-resource bounds

Argon2 work is bounded by configured memory, concurrent work, queue length, and timeout limits. Roles, permissions, token sizes, refresh families, request bodies, audit details, OIDC state/metadata, and return URLs are also bounded by validated options and request contracts. Cryptographic cost is not reduced automatically under load.

## Controlled Windows evidence

`scripts/performance-evidence.ps1` records revision-bound aggregate measurements for hashing, JWT work, authorization-context construction, login, persisted-state validation, refresh rotation and replay contention, SQLite and PostgreSQL pagination, and role invalidation.

Evidence includes warm-up and repeated measured iterations, p50, p95, maximum duration, allocations per operation, working-set delta, configured Argon2 memory, token size, and bounded test-data scale. It excludes credentials, raw tokens, identifiers, connection strings, SQL, and exception messages.

The controlled reference environment record must include CPU, memory, Windows version, power plan, .NET SDK/runtime, PostgreSQL version and material configuration, SQLite/native versions, storage, and dataset scale. CPU-bound cryptographic evidence must be distinguishable from database and network evidence.

`eng/PerformanceBaseline.json` is approved only after a reviewed run on that documented environment. The baseline must record the exact approved revision, reference environment, required metric set, tolerance, reviewer, and refresh policy. Once approved, the gate rejects missing metrics and p95/allocation regressions outside the configured tolerance.

An approval remains valid only when its exact approved revision is the selected release revision or an ancestor and every later changed path is permitted by the baseline policy. Tree equivalence is not sufficient evidence identity. In particular, a feature-branch approval does not survive a squash merge when the approved commit is no longer an ancestor of the merged revision.

After implementation is merged, capture and review the controlled candidate on the exact protected revision. Commit the approval through a baseline-only pull request whose base is that approved revision, then run the complete protected release-candidate gate on the resulting exact revision. Do not delete evidence-referenced branches until their approvals are reachable from protected history or have been explicitly invalidated and replaced.

A baseline with status `pending-reference-run`, no reachable approved revision, or no metrics blocks `v0.9.0-rc.1`. Values must not be estimated, copied from an uncontrolled run, or silently reused after history rewriting.

The historical baseline approved on pre-squash revision `56f1284d4c0f307c2be3c7e712bd32bcfef1cae7` was invalidated when PR #135 produced squash commit `ad9c5b4dd503fe8356d6ba450db4705fe2b94200`. Later release-evidence repairs and OIDC orchestration changes were followed by a fresh controlled candidate on `0726fe5915a0ca9a71338eae68ed2d28c2a92297`. Baseline-only PR #145 integrated that approval as `eb083a34e19ffb1eb5c6e84050beb977a347b48d`, and the complete protected development-repository matrix passed on that exact revision. The final release-state documentation synchronization creates a new selected revision when merged, so exact-revision policy requires one final controlled candidate, baseline-only integration, and protected matrix before canonical export.

## PostgreSQL query plans

Retain representative native query plans and latency for first and continuation pages at limits 1, 100, and 200, including equal timestamps and multi-role members. Plans must use the intended indexes and avoid unbounded scans or depth-proportional offsets. This is a continuing Supported-provider obligation for release revisions, not a one-time promotion artifact.

SQL Server and MySQL have no active performance obligations; future implementations must establish new baselines and native Windows evidence.

Reference evidence is not a production SLA. Hosts must validate representative hardware, database topology, data volume, encryption, network latency, and failure conditions. Multi-instance hosts must share and persist Data Protection keys so cursor continuation remains valid during deployment and load tests.
