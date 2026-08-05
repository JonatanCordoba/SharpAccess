# SharpAccess documentation

SharpAccess is a Windows-only .NET 10 package family. Repository automation uses PowerShell 7. Current package/provider status is owned by `eng/ProviderStatus.props`; the synchronized package version is owned by `eng/Version.props`.

## Package consumers

- [Package overview and installation](NUGET-PACKAGE.md)
- [Architecture](ARCHITECTURE.md)
- [Public API](PUBLIC-API.md)
- [Authorization model](AUTHORIZATION.md)
- [OAuth and OpenID Connect](OAUTH.md)
- [Database providers](DATABASE-PROVIDERS.md)
- [Migrations](MIGRATIONS.md)
- [Provider status](PROVIDER-STATUS.md)

## Provider setup and operations

- [Persistence and connections](PERSISTENCE-AND-CONNECTIONS.md)
- [PostgreSQL operations](POSTGRES-OPERATIONS.md)
- [PostgreSQL promotion decision and evidence](POSTGRES-PROMOTION.md)
- [Backup and restore](BACKUP-RESTORE.md)
- [Operations](OPERATIONS.md)
- [Observability](OBSERVABILITY.md)
- [Capacity planning](CAPACITY-PLANNING.md)
- [Performance evidence](PERFORMANCE.md)

## Security and governance

- [Security](SECURITY.md)
- [Threat model](THREAT_MODEL.md)
- [Access-token signing keys](SIGNING-KEYS.md)
- [Privacy](PRIVACY.md)
- [Incident response](INCIDENT-RESPONSE.md)
- [Business continuity](BUSINESS-CONTINUITY.md)
- [Change management](CHANGE-MANAGEMENT.md)
- [Repository governance](repository-governance.md)
- [Architecture decisions](adr/README.md)

## Contributors and maintainers

- [Testing](TESTING.md)
- [Quality gates](QUALITY-GATES.md)
- [Engineering-quality report](QUALITY-REPORT.md)
- [Provider contract testing](PROVIDER-CONTRACT-TESTING.md)
- [Provider evidence](PROVIDER-PARITY-EVIDENCE.md)

## Release engineering

- [Versioning](VERSIONING.md)
- [Roadmap](ROADMAP.md)
- [Release checklist](RELEASE-CHECKLIST.md)
- [Integrated release-candidate evidence](RELEASE-CANDIDATE.md)
- [Release evidence matrix](RELEASE-EVIDENCE-MATRIX.md)
- [Clean release-repository bootstrap](RELEASE-REPOSITORY-BOOTSTRAP.md)
- [Supply chain](SUPPLY-CHAIN.md)

## Historical material

Accepted and superseded decisions remain in [`adr`](adr/README.md). `POSTGRES-PROMOTION.md` remains historical decision/evidence documentation while `POSTGRES-OPERATIONS.md` owns continuing operations. Pre-release patch plans and duplicate release ledgers are intentionally excluded from the clean public tree; internal prompts and audit reports are also excluded. Executable migration fixtures and current compatibility documentation remain authoritative.

## Ownership rules

- `eng/ProviderStatus.props` owns active package status.
- `eng/Version.props` owns the synchronized package version.
- `docs/ROADMAP.md` owns future provider and phase sequencing.
- `docs/PROVIDER-PARITY-EVIDENCE.md` owns current provider evidence classification.
- `docs/POSTGRES-PROMOTION.md` and ADR 0021 own the PostgreSQL promotion decision and historical exact-revision procedure.
- `docs/POSTGRES-OPERATIONS.md` owns continuing PostgreSQL operational guidance.
- `docs/TESTING.md` owns test commands, taxonomy, and fixtures.
- `docs/QUALITY-GATES.md` owns enforced coverage, changed-code, complexity, mutation, and release-quality gates.
- `docs/QUALITY-REPORT.md` owns quality-report schema, outputs, metrics, and interpretation.
- `docs/RELEASE-CHECKLIST.md` owns RC and stable release completion.
- ADRs own accepted decisions and supersession history.
- The root README summarizes these contracts without duplicating operational detail.
