# Security policy

## Supported versions

`0.9.0-rc.1` is the currently published SharpAccess prerelease. Security fixes target protected `main` first. Backports are made only for release lines explicitly listed as maintained.

Stable `1.0.0` is not yet a released or active execution stage.

Provider support follows `eng/ProviderStatus.props`. Core, SQLite, and PostgreSQL are Supported. SQL Server and MySQL are deferred roadmap candidates and are not supported release surfaces.

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Use GitHub private vulnerability reporting or a private GitHub Security Advisory for this repository. Include the affected package, version or commit, impact, safe reproduction steps, and whether credentials, tokens, tenant isolation, OAuth/OIDC, password recovery, refresh-token rotation, provider boundaries, or authorization are affected. Never include real production credentials, personal data, or active tokens.

## Response targets

Acknowledgment, triage, remediation, exception, and coordinated-disclosure targets are defined in [`docs/VULNERABILITY-MANAGEMENT.md`](docs/VULNERABILITY-MANAGEMENT.md). They are operational targets rather than guarantees; active exploitation or ecosystem impact can shorten the schedule.

## Handling process

Maintainers acknowledge the report, assess severity and affected versions, prepare a private fix, run applicable verification gates, publish signed provenance and SBOM evidence for fixed release artifacts when a release is required, and coordinate disclosure after users have a reasonable opportunity to update. Emergency exceptions must be recorded and followed by complete validation.

## Technical security documentation

See [`docs/SECURITY.md`](docs/SECURITY.md), [`docs/THREAT_MODEL.md`](docs/THREAT_MODEL.md), [`docs/SECURITY-AND-CI-HARDENING.md`](docs/SECURITY-AND-CI-HARDENING.md), [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md), and the durable RC1 identity ledger in [`docs/RELEASE-EVIDENCE-MATRIX.md`](docs/RELEASE-EVIDENCE-MATRIX.md).

## Scope notes

Deployment-specific misconfiguration, weak consumer-managed secrets, compromised host infrastructure, and unsupported forks may be outside package scope unless SharpAccess directly creates or enables the unsafe state.
