# Operations

SharpAccess is a package family, not a hosted service. The package supplies secure defaults, bounded diagnostic signals, release evidence, capacity controls, and recovery drills. The consuming host remains responsible for deployment architecture, secrets, networking, database operations, alert routing, retention, and on-call response.

## Operational ownership

Assign named owners before production use:

| Area | Package responsibility | Host responsibility |
|---|---|---|
| Authentication behavior | Secure package implementation and compatibility | Configuration, feature selection, user support |
| Relational persistence | Provider-owned schema and transactions | Database availability, backup, restore, capacity, credentials |
| Secrets and keys | Validation and safe use | Generation, storage, rotation, revocation |
| Telemetry | Bounded `ActivitySource` and `Meter` signals | Exporters, sampling, dashboards, alerts, retention |
| Incidents | Documented package response procedures | On-call, communications, containment, recovery |
| Releases | Packages, checksums, SBOMs, provenance | Approval, deployment, rollback, consumer validation |

## Required operational evidence

Keep evidence for the period defined by policy and regulation. At minimum retain:

- the source commit and review record;
- required CI and security-check results;
- package hashes, SBOMs, and provenance attestations;
- the operational-readiness and release-candidate indexes;
- PostgreSQL promotion evidence when that package is in the release cohort;
- performance and capacity evidence;
- deterministic export and tree-equivalence evidence;
- recovery-drill records;
- approved change or emergency-change records;
- risk exceptions and expiry dates;
- incident timelines, postmortems, and corrective actions.

`scripts/operational-readiness.ps1` produces operational evidence under `artifacts/operations`. The integrated release-candidate entry point is documented in `RELEASE-CANDIDATE.md`.

## Production preparation

Before enabling SharpAccess for real users:

1. Complete `docs/RELEASE-CHECKLIST.md`.
2. Define host-specific service-level objectives and alerts.
3. Review `docs/CAPACITY-PLANNING.md` and test representative load.
4. Configure persistent Data Protection keys.
5. Store JWT keys, token-hashing keys, peppers, OAuth credentials, SMTP credentials, and database credentials outside the repository.
6. Test database backup and restoration using provider-appropriate production tooling.
7. Establish log and audit retention without recording secrets or raw tokens.
8. Configure on-call ownership and incident communications.
9. Verify rollback steps for application, package, configuration, and database changes.

## Operational changes

Use `docs/CHANGE-MANAGEMENT.md` and `docs/templates/CHANGE-RECORD.md` for security-sensitive, release, persistence, or configuration changes. Emergency changes must preserve evidence and receive retrospective review.

## Provider status

`SharpAccess.Postgres` is Supported only through the exact-revision gate in `POSTGRES-PROMOTION.md`. SQL Server and MySQL are absent from the active tree and remain roadmap candidates without active operational or release obligations.