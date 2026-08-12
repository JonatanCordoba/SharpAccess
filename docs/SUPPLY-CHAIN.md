# Supply-chain controls

SharpAccess supply-chain controls are Windows-only and PowerShell 7-only and apply to the active Core, SQLite, and PostgreSQL cohort.

## Dependency and action controls

- Restore uses lock files and locked mode on controlled verification/release paths.
- Dependency review is a required pull-request control; the repository may use an explicitly documented NuGet audit fallback where applicable, but blocked native evidence is never described as passing native dependency-review evidence.
- External GitHub Actions are pinned to reviewed full commit SHAs.
- DevSkim is the blocking SAST control.
- Tracked-secret scanning and repository secret controls remain independent gates.

## Package and SBOM controls

Core, SQLite, and PostgreSQL are Supported and each participates in package validation, package-consumer validation, deterministic SBOM generation, checksums, and provenance for applicable releases.

PostgreSQL is not an incubation/package-root exception. Its package and consumer evidence is a continuing Supported-provider obligation.

SQL Server and MySQL are not active packages or supply-chain targets.

## Published RC1

RC1 publication is complete. The validated release artifact for immutable commit `4595545d8afd84c58795fc02c2c242533cdff1ac` was consumed by the protected NuGet Trusted Publishing workflow without rebuilding package bytes. The durable release/artifact/package/publication identities are in `RELEASE-EVIDENCE-MATRIX.md`.

Post-release documentation changes do not regenerate or replace those immutable package/SBOM/provenance identities.

## Future releases

For a future release, retain exact revision, package archive hashes, deterministic SBOMs, checksum manifests, provenance, package-consumer evidence, and protected publication evidence as required by the selected policy. Missing, expired, blocked, skipped, or not-run evidence is not success.
