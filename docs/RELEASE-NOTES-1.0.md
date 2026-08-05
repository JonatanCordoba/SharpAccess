# SharpAccess 1.0 release notes

## Release scope

SharpAccess 1.0 is a Windows-only .NET 10 package family.

The stable package cohort is:

- `SharpAccess.Core`;
- `SharpAccess.Sqlite`;
- `SharpAccess.Postgres`, after its coordinated promotion gate passes.

SQL Server and MySQL are not included in the active source tree or stable package set. They remain future roadmap candidates without compatibility or delivery commitments.

## Platform and tooling

- Windows is the supported engineering, CI, release, and deployment platform.
- Repository automation uses PowerShell 7 only.
- Bash parity and Linux/macOS workflows were removed.
- Docker, Compose, service containers, and local container orchestration were removed.
- PostgreSQL release evidence uses native Windows client tools and an approved native or managed scratch database.

## API stabilization

- Removed all `DotNetAuth` registration and application aliases.
- Removed `AddSqliteAuth` and other pre-v1 compatibility names.
- Removed legacy bearer-scheme, refresh-cookie, and CSRF-header fallbacks.
- Replaced unscoped authorization aliases with explicit global or active-tenant attributes.
- Kept SQLite as the supported provider registration path.
- Kept PostgreSQL registration internal until promotion.
- Removed SQL Server/MySQL options, namespaces, registrations, and package identities from the active product surface.

## Security and authorization

- Global and tenant authorization are separate persisted and token domains.
- Tenant authority is bound to the active route tenant.
- JWT validation rechecks account, security-version, authorization-version, and membership state.
- Refresh tokens are opaque, keyed-hashed, rotated, replay-detected, and family-revoked transactionally.
- Access-token signing uses a mandatory rotatable key ring.
- Passwords use Argon2id with versioned host-owned peppers.
- Pagination cursors are opaque, purpose-bound Data Protection values.

## Release integrity

Stable packages are published only from one verified signed root commit in the clean `JonatanCordoba/SharpAccess` repository. The root tree must match the approved development revision exactly and includes locked dependencies, package smoke evidence, SBOMs, checksums, provenance, operations, recovery, and security evidence.
