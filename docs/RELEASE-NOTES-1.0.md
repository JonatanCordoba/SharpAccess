# SharpAccess 1.0 release notes — future draft

Stable `1.0.0` has not started as an execution or publication stage. This file is a future release-notes draft and does not claim that `1.0.0` is released or ready.

## Current baseline inherited from RC1

The currently published prerelease `0.9.0-rc.1` establishes a Windows-only, PowerShell 7 engineering/release model with Supported Core, SQLite, and PostgreSQL packages.

The current public surface includes provider-neutral Core registration/behavior plus `AddSqliteAccess` and `AddPostgresAccess`. PostgreSQL promotion is complete; it is not waiting on a future promotion gate.

## Candidate stable themes

If/when stable work is explicitly opened, release notes should describe the actual selected stable revision and only changes/evidence that exist at that time. Expected review areas include:

- pre-1.0 compatibility changes and removed aliases;
- explicit global versus tenant authorization semantics;
- bounded cursor pagination and security-resource limits;
- key/token/pepper rotation contracts;
- OIDC provider-neutral behavior;
- SQLite/PostgreSQL persistence, migrations, recovery, and operations;
- package, SBOM, provenance, security, and clean-consumer validation;
- RC feedback/defect disposition.

Do not predeclare stable readiness, tag identity, package bytes, or final evidence before the stable stage is opened and executed.
