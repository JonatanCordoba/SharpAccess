# Security policy

## Supported versions

SharpAccess is pre-release until the first public NuGet release is published. Security fixes target the default branch first. Backports are made only for release lines explicitly listed as maintained.

Provider support follows `eng/ProviderStatus.props`. Reports involving providers marked **Internal implementation in progress**, **Roadmap**, or **Unsupported** are evaluated, but those providers are not represented as supported release surfaces.

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Use GitHub private vulnerability reporting or a private GitHub Security Advisory for this repository. Include the affected package, version or commit, impact, safe reproduction steps, and whether credentials, tokens, tenant isolation, OAuth, password recovery, refresh-token rotation, provider boundaries, or authorization are affected. Never include real production credentials, personal data, or active tokens.

## Response targets

The acknowledgment, triage, remediation, exception, and coordinated-disclosure targets are defined in [`docs/VULNERABILITY-MANAGEMENT.md`](docs/VULNERABILITY-MANAGEMENT.md). These are operational targets rather than guarantees; active exploitation or ecosystem impact can shorten the schedule.

## Handling process

Maintainers acknowledge the report, assess severity and affected versions, prepare a private fix, run the applicable verification gates, publish signed provenance and SBOM evidence for release artifacts, and coordinate disclosure after users have a reasonable opportunity to update. Emergency exceptions must be recorded and followed by complete validation.

## Technical security documentation

See [`docs/SECURITY.md`](docs/SECURITY.md), [`docs/THREAT_MODEL.md`](docs/THREAT_MODEL.md), [`docs/SECURITY-AND-CI-HARDENING.md`](docs/SECURITY-AND-CI-HARDENING.md), and [`docs/RELEASE-INTEGRITY.md`](docs/RELEASE-INTEGRITY.md).

## Scope notes

Deployment-specific misconfiguration, weak consumer-managed secrets, compromised host infrastructure, and unsupported forks may be outside package scope unless SharpAccess directly creates or enables the unsafe state.
