# Architecture decision records

Use this directory for decisions that materially affect package identity, public APIs, provider boundaries, persistence contracts, security behavior, compatibility, migrations, testing gates, supported platforms, or release controls.

## Accepted decisions

| ADR | Decision |
|---|---|
| [0001](0001-provider-neutral-core.md) | Keep the core provider-neutral. |
| [0002](0002-stable-1-0-package-set.md) | Historical coordinated five-package 1.0 decision; superseded by ADR 0015 and ADR 0020. |
| [0003](0003-coordinated-provider-release.md) | Historical coordinated provider-promotion decision; superseded by ADR 0015 and ADR 0020. |
| [0004](0004-net10-only-support.md) | Target .NET 10 only for stable 1.0. |
| [0005](0005-agpl-3-only-license.md) | Historical AGPL-3.0-only decision; superseded by ADR 0016. |
| [0006](0006-fixed-auth-object-ownership.md) | Own fixed `auth_*` database objects for v1. |
| [0007](0007-separate-global-and-tenant-authorization.md) | Separate global and tenant authorization catalogs. |
| [0008](0008-migration-modes-and-production-default.md) | Expose explicit migration modes with a safe production default. |
| [0009](0009-host-managed-data-sources.md) | Support host-managed connections and data sources. |
| [0010](0010-bounded-jwt-authorization-context.md) | Bound JWT authorization context with reference fallback. |
| [0011](0011-mandatory-signing-key-ring.md) | Require a rotatable JWT signing key ring. |
| [0012](0012-clean-release-repository.md) | Publish from a clean release repository. |
| [0013](0013-atomic-security-audit-evidence.md) | Commit mandatory security audit evidence atomically. |
| [0014](0014-generic-keyed-openid-connect.md) | Use a generic keyed OpenID Connect contract with a Google-compatible default entry. |
| [0015](0015-initial-stable-release-cohort.md) | Release Core, SQLite, and PostgreSQL as the initial stable cohort. |
| [0016](0016-mit-license.md) | License SharpAccess under MIT. |
| [0017](0017-opaque-refresh-tokens.md) | Use opaque refresh tokens with rotation and family revocation. |
| [0018](0018-use-case-boundaries.md) | Route endpoint-facing authentication through focused internal use cases. |
| [0019](0019-windows-only-release-toolchain.md) | Use Windows and PowerShell 7 as the only supported engineering and release toolchain; prohibit container orchestration. |
| [0020](0020-active-provider-cohort.md) | Keep only Core, SQLite, and PostgreSQL in the active repository cohort; retain SQL Server and MySQL as roadmap candidates only. |
| [0021](0021-postgresql-support-promotion.md) | Promote PostgreSQL through one exact-revision provider-specific evidence gate. |

Each ADR should record:

- status and decision date;
- context and constraints;
- considered options when alternatives materially affect the decision;
- the selected decision and rationale;
- security, compatibility, provider, test, documentation, and rollback consequences.

ADRs document decisions; current implementation and authoritative project contracts remain the source of truth when an older ADR no longer matches the repository.