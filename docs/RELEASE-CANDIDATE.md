# Release candidate

## Purpose

This document records the completed `SharpAccess` `0.9.0-rc.1` release-candidate and protected publication process and preserves the reusable release-control model for future releases.

`JonatanCordoba/SharpAccess` is the canonical source, verification, release, and publication repository. Release engineering is Windows-only and PowerShell 7-only.

## Published RC1 identity

RC1 publication is complete.

- version: `0.9.0-rc.1`
- signed annotated tag: `v0.9.0-rc.1`
- immutable package provenance commit: `4595545d8afd84c58795fc02c2c242533cdff1ac`
- annotated tag object: `8660c43aada304c7f64c271a9bfbebfbe2ac6c55`
- integrated release-candidate workflow run: `31340946355`
- validated artifact ID: `9046031931`
- artifact name: `release-candidate-4595545d8afd84c58795fc02c2c242533cdff1ac`
- artifact digest: `sha256:f7b81fe2d7d31ee298e2412da691831db9dbcc5389b99c60290ad06f31f86402`
- NuGet Trusted Publishing run: `31343342871`
- GitHub prerelease ID: `367623569`

Current `main` may be newer than the release commit. Later documentation, Wiki, governance, or closure changes do not alter the provenance of the already-published RC1 package bytes.

The durable authoritative identity inventory is `docs/RELEASE-EVIDENCE-MATRIX.md`.

## Published package cohort

The published RC1 cohort is exactly:

- `SharpAccess.Core` `0.9.0-rc.1` plus symbols;
- `SharpAccess.Sqlite` `0.9.0-rc.1` plus symbols;
- `SharpAccess.Postgres` `0.9.0-rc.1` plus symbols.

All three packages use MIT package license metadata. Public discovery, published nuspec metadata, clean-consumer restore/build, and version-skew checks passed for the published cohort.

SQL Server and MySQL are not active release targets.

## Release evidence model

The integrated candidate gate is exact-revision evidence. Required stages fail rather than silently skipping. Missing, expired, environment-blocked, or not-run evidence is not passing evidence.

For applicable future release candidates, the integrated entry point remains:

```powershell
./scripts/release-candidate.ps1 `
  -RepositoryRoot $PWD `
  -Version '<candidate-version>' `
  -ReferenceEnvironment '<approved-controlled-windows-environment>' `
  -RequirePostgres `
  -RequireOidcLiveEvidence `
  -RequireApprovedPerformanceBaseline
```

The requested version must agree with `eng/Version.props`.

The complete evidence model includes clean-tree verification, Supported-provider coverage, engineering-quality evidence, controlled performance, PostgreSQL real-engine contracts/recovery, protected OIDC, deterministic export evidence, checksums, package validation, SBOMs, and a revision-bound evidence index as required by the selected release policy.

## Protected publication model

`.github/workflows/publish-nuget.yml` is publication-only. It consumes the exact validated release-candidate artifact for the signed release-tag commit and does not rebuild or repack package bytes.

Publication uses the protected `nuget-release` boundary and NuGet Trusted Publishing with GitHub OIDC. A long-lived NuGet API key is not part of the SharpAccess publication contract.

The existence of the workflow does not authorize a future release; tagging and publication remain separately authorized release actions.

## Post-release maintenance

Do not rerun RC1 NuGet publication, move/recreate the RC1 tag, run unpublished-version assertions as a post-publication success gate, or recapture immutable RC1 PostgreSQL/OIDC/performance/package evidence merely because current documentation or repository controls are synchronized.

## Stable boundary

Stable `1.0.0` has not started as an execution stage. RC1 evidence does not automatically authorize stable publication. A stable stage requires explicit opening, a newly selected exact revision, and the then-current stable evidence matrix.
