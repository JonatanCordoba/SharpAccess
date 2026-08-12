# Security and CI hardening

SharpAccess uses independent Windows-only repository controls for static analysis, dependency assurance, secret detection, action pinning, locked dependencies, package/SBOM validation, and release evidence.

## Current gates

- Microsoft DevSkim is the blocking SAST gate.
- The DevSkim workflow uses least-privilege repository permissions and retains reviewed output as configured.
- Dependency review is a required pull-request check; any documented NuGet audit fallback is a compensating control, not equivalent native GitHub dependency-review evidence.
- Tracked-secret scanning is a required pull-request check, with repository secret scanning/push protection maintained as GitHub settings state where available.
- Every external GitHub Action reference is pinned to a full reviewed commit SHA.
- Restore on controlled verification/release paths uses lock files and locked mode.
- Provider evidence is status-driven: SQLite and PostgreSQL are Supported; SQL Server/MySQL are deferred.
- Package validation, deterministic SBOMs, checksums, and provenance apply to the active release cohort.

The repository does not maintain Bash, Linux/macOS, Docker, Compose, service-container, or service-image parity.

## Required-check policy

Protected `main` uses nine pull-request checks: PR evidence, SQLite provider evidence, Windows CI, DevSkim, operational readiness, provider-contract classifier, dependency review, test-scope classifier, and tracked-secret scan. `.github/required-checks.json` must remain synchronized with the live settings policy.

## Evidence semantics

A configured control is not passing evidence when execution failed, was blocked before execution, was skipped, expired, or did not run. Do not weaken a gate to make a change pass.

RC1 publication is complete. Current post-release documentation/control synchronization uses ordinary protected PR checks and the required exact-commit local verification; it does not rerun immutable RC1 protected release evidence merely to refresh prose.
