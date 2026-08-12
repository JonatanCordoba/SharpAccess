# Operations

SharpAccess is a package family, not a hosted service. The package supplies secure defaults, bounded diagnostics, release evidence, capacity controls, and recovery drills; the consuming host owns deployment architecture, secrets, networking, database operations, alert routing, retention, and on-call response.

## Evidence ownership

Retain applicable source/review records, CI/security results, package hashes/SBOM/provenance, operational-readiness and release indexes, continuing PostgreSQL evidence, performance/capacity evidence, recovery records, approved changes/risk exceptions, and incident follow-up according to policy.

PostgreSQL is Supported. Applicable future release revisions require the continuing real-engine/provider/recovery/package obligations described in `PROVIDER-PARITY-EVIDENCE.md` and `POSTGRES-OPERATIONS.md`. `POSTGRES-PROMOTION.md` is historical promotion context, not a pending support gate.

## Production preparation

Before enabling SharpAccess for real users:

1. define host-specific service objectives and alerts;
2. review `CAPACITY-PLANNING.md` with representative load;
3. persist/protect Data Protection keys;
4. store JWT/token-hashing/rate-limit/pepper/OIDC/SMTP/database secrets outside the repository;
5. test provider-appropriate backup/restore;
6. establish log/audit retention without secrets;
7. configure on-call/incident ownership;
8. verify application/package/config/database rollback procedures.

The RC1 release checklist is historical release state plus future stable criteria; it is not a requirement to rerun published RC1 evidence before deploying an already-published package.

## Change management

Use `CHANGE-MANAGEMENT.md` and the change-record template for security-sensitive, release, persistence, or configuration changes. Emergency changes must preserve evidence and receive retrospective review.

SQL Server and MySQL remain deferred and have no active operational/release obligations.
