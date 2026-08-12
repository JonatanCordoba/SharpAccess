# SharpAccess documentation

This is the documentation entry point for the current SharpAccess repository.

## Mutable fact owners

- Active provider/package status: `eng/ProviderStatus.props`.
- Synchronized package version: `eng/Version.props`.
- Canonical package license metadata: `Directory.Build.props`.
- Product/future-provider roadmap vocabulary: `docs/ROADMAP.md`.
- Active provider evidence classification: `docs/PROVIDER-PARITY-EVIDENCE.md`.
- Historical PostgreSQL promotion procedure/decision: `docs/POSTGRES-PROMOTION.md` and ADR 0021.
- Continuing PostgreSQL operations: `docs/POSTGRES-OPERATIONS.md`.
- Durable RC1 release identity/evidence ledger: `docs/RELEASE-EVIDENCE-MATRIX.md`.
- Published RC1 process record: `docs/RELEASE-CANDIDATE.md`.
- Future stable-release completion checklist: `docs/RELEASE-CHECKLIST.md`.
- Historical clean-root migration and legacy-decommission boundary: `docs/RELEASE-REPOSITORY-BOOTSTRAP.md`.

`0.9.0-rc.1` is published. Current `main` may advance after publication without changing the immutable RC1 package provenance commit. Stable `1.0.0` is a future, separately opened stage.

## Product and operator guides

- Architecture: `ARCHITECTURE.md` and `architecture/`.
- Authorization: `AUTHORIZATION.md`, `ATTRIBUTES.md`, `PUBLIC-API.md`.
- Providers and persistence: `DATABASE-PROVIDERS.md`, `PERSISTENCE-AND-CONNECTIONS.md`, `MIGRATIONS.md`, `SQLITE.md`, `POSTGRES-OPERATIONS.md`.
- Testing and quality: `TESTING.md`, `QUALITY-GATES.md`, `QUALITY-REPORT.md`.
- Security: `SECURITY.md`, `THREAT_MODEL.md`, `CRYPTOGRAPHY.md`, `SIGNING-KEYS.md`, `RATE-LIMITING.md`, `SECURITY-AND-CI-HARDENING.md`.
- Operations: `DEPLOYMENT.md`, `OPERATIONS.md`, `BACKUP-RESTORE.md`, `BUSINESS-CONTINUITY.md`, `OBSERVABILITY.md`, `CAPACITY-PLANNING.md`, `production-hardening.md`.
- Package/release: `NUGET-PACKAGE.md`, `SUPPLY-CHAIN.md`, `VERSIONING.md`, `package-consumer-validation.md`.

## Documentation lifecycle

Current-state documentation must describe the active tree and actual release lifecycle. Historical ADRs and evidence records may preserve superseded chronology when it explains current contracts.

Pre-release patch plans are disposable working material and must not become a second current-state roadmap. Do not create duplicate release ledgers when the durable identity ledger already owns the fact. Historical clean public tree/bootstrap procedures must be clearly marked as completed history, not executable current instructions.

When documentation is consolidated, preserve unique current facts before removing a file. Accepted and superseded ADRs remain when they explain current contracts; executable historical migration fixtures remain when compatibility tests require them.
