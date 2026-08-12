# Package consumer validation

## Goal

Prove that the Supported prerelease package cohort can be consumed through package references rather than project references.

For RC1 the cohort is exactly:

- `SharpAccess.Core` `0.9.0-rc.1`;
- `SharpAccess.Sqlite` `0.9.0-rc.1`;
- `SharpAccess.Postgres` `0.9.0-rc.1`.

## Repository package-smoke behavior

`scripts/package-smoke.ps1` validates that the expected runtime package archives exist, creates temporary consumer projects, references packages from the produced package directory, restores in locked/reviewed conditions as configured, and compiles the reviewed public registration surface.

The consumer exercises Core plus SQLite registration and separately compiles PostgreSQL registration so package dependencies/public API are proven without a project reference. Real-engine PostgreSQL behavior remains provider-contract evidence rather than package-consumer compilation evidence.

No SQL Server or MySQL package belongs to the active consumer cohort.

## Published RC1 validation

Public discovery, published nuspec metadata, clean-consumer restore, clean-consumer Release build, and SharpAccess version-skew validation passed for the published RC1 cohort. Those results are retained in `RELEASE-EVIDENCE-MATRIX.md`.

Post-release documentation changes do not require republishing packages or rerunning the unpublished-version check against an already-published RC1 version.

## Future releases

A future release must run the package-consumer checks required by its selected policy against the exact package bytes intended for publication, then repeat the applicable clean-consumer/public-feed checks after publication.
