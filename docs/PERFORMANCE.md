# Performance contract

SharpAccess bounds collection and security-resource work by contract rather than caller convention.

## Bounded behavior

Administrative/tenant lists use keyset pagination with a maximum page size of 200. Providers fetch at most `limit + 1` logical keys and use seekable continuation predicates; offset pagination is not part of the reviewed contract.

Argon2 work is bounded by configured memory, concurrent work, queue length, and timeout limits. Roles, permissions, token sizes, refresh families, request bodies, audit details, OIDC state/metadata, and return URLs are also bounded by validated options and request contracts. Cryptographic cost is not reduced automatically under load.

## Controlled Windows evidence

`scripts/performance-evidence.ps1` records revision-bound aggregate measurements for hashing, JWT work, authorization context, login/state validation, refresh rotation/replay contention, SQLite/PostgreSQL keyset pagination, role invalidation, allocations, and bounded resource metadata.

The controlled profile records a sanitized Windows environment fingerprint including CPU/memory, power plan, .NET runtime/SDK, PostgreSQL and SQLite/native versions, storage characteristics, and dataset scale. Evidence excludes credentials, raw tokens, identifiers, connection strings, SQL, and exception messages.

Each controlled capture uses the repository-defined independent-process protocol and retains the reviewed aggregate/individual observations required by `eng/ReleaseCandidate.props`.

## Baseline lifecycle

A performance approval is exact-revision evidence. A future candidate baseline must be reviewed on the documented controlled Windows environment and is valid only under the revision/ancestry/path scope accepted by the current policy. Tree equivalence alone is not evidence identity.

Use the repository-supported capture/approval workflow when a future release needs a new baseline. Do not approve values copied from an uncontrolled run or captured on an unstable environment.

## Published RC1

RC1 performance evidence is historical, completed release evidence. It contributed to the immutable RC1 release decision for package provenance commit `4595545d8afd84c58795fc02c2c242533cdff1ac`.

Post-release documentation, Wiki, governance, or branch-cleanup changes do **not** require a new controlled performance capture merely because `main` advanced. A new capture is required only when a future selected release/evidence policy requires it.

## PostgreSQL query plans

Representative native query plans for bounded first/continuation pages remain a continuing Supported PostgreSQL obligation on applicable future release revisions. Plans must use intended indexes and avoid unbounded scans or depth-proportional offsets.

SQL Server and MySQL have no active performance obligations. Future implementations require new evidence.

Reference evidence is not a production SLA. Hosts must validate representative hardware, database topology, data volume, encryption, network latency, and failure conditions.
