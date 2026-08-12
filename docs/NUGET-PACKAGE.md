# NuGet packages

## Published RC1 cohort

The public prerelease cohort is:

```powershell
dotnet add package SharpAccess.Core --version 0.9.0-rc.1
dotnet add package SharpAccess.Sqlite --version 0.9.0-rc.1
# or, instead of SQLite:
dotnet add package SharpAccess.Postgres --version 0.9.0-rc.1
```

Install Core plus exactly one supported relational provider.

`SharpAccess.Core`, `SharpAccess.Sqlite`, and `SharpAccess.Postgres` version `0.9.0-rc.1` are published on nuget.org with matching symbol packages. Public discovery, published nuspec metadata, clean-consumer restore/build, and SharpAccess version-skew validation passed.

All three packages use MIT package license metadata owned by `Directory.Build.props`.

## Package metadata

Supported package projects generate XML documentation, include their package README, carry repository/commit metadata, and are validated through package tests and package-consumer smoke. Source Link and symbols must correspond to the exact package source revision.

SQL Server and MySQL package IDs are not active.

## RC1 provenance

The immutable RC1 package provenance commit is `4595545d8afd84c58795fc02c2c242533cdff1ac` and the signed tag is `v0.9.0-rc.1`. Later `main` documentation or governance commits do not change the source identity of those package bytes.

The definitive release-candidate artifact is ID `9046031931`, name `release-candidate-4595545d8afd84c58795fc02c2c242533cdff1ac`, digest `sha256:f7b81fe2d7d31ee298e2412da691831db9dbcc5389b99c60290ad06f31f86402` from workflow run `31340946355`.

NuGet publication completed in run `31343342871` through NuGet Trusted Publishing with GitHub OIDC. The GitHub prerelease ID is `367623569`.

## Future package creation and publication

`scripts/pack.ps1`, package validation, SBOM generation, checksum/provenance evidence, and clean package-consumer validation remain required by the applicable future release policy.

`.github/workflows/publish-nuget.yml` is publication-only: it consumes the exact validated release artifact for the signed release-tag commit and must not rebuild or repack the cohort.

A long-lived NuGet API key is not part of the SharpAccess publication contract. Future releases require separate tagging/publication authorization.

Do not run an unpublished-version assertion against `0.9.0-rc.1` as a post-publication success gate; the expected state is that the version already exists.
