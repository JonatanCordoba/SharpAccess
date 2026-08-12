# Release evidence matrix

## Purpose

This file is the durable identity and evidence ledger for the published `0.9.0-rc.1` release. It records immutable release identities separately from later post-release repository state.

## Immutable RC1 identities

| Evidence | Final identity / result |
|---|---|
| Repository | `JonatanCordoba/SharpAccess` |
| Version | `0.9.0-rc.1` |
| Signed tag | `v0.9.0-rc.1` |
| RC1 package provenance commit | `4595545d8afd84c58795fc02c2c242533cdff1ac` |
| Annotated tag object | `8660c43aada304c7f64c271a9bfbebfbe2ac6c55` |
| Integrated RC workflow run | `31340946355` — success |
| RC artifact ID | `9046031931` |
| RC artifact name | `release-candidate-4595545d8afd84c58795fc02c2c242533cdff1ac` |
| RC artifact digest | `sha256:f7b81fe2d7d31ee298e2412da691831db9dbcc5389b99c60290ad06f31f86402` |
| Publication workflow run | `31343342871` — success via NuGet Trusted Publishing / GitHub OIDC |
| GitHub prerelease | ID `367623569`, `SharpAccess 0.9.0-rc.1`, prerelease, not draft |
| License authority | `MIT` through `Directory.Build.props` package license expression |

## Published package cohort

| Package | Version | Runtime package | Symbols | License | Public validation |
|---|---|---|---|---|---|
| `SharpAccess.Core` | `0.9.0-rc.1` | published | published | MIT | public discovery, nuspec metadata, clean consumer restore/build passed |
| `SharpAccess.Sqlite` | `0.9.0-rc.1` | published | published | MIT | public discovery, nuspec metadata, clean consumer restore/build passed |
| `SharpAccess.Postgres` | `0.9.0-rc.1` | published | published | MIT | public discovery, nuspec metadata, clean consumer restore/build passed |

The validated published cohort has no SharpAccess package-version skew.

## Wiki publication identities

| Evidence | Identity / result |
|---|---|
| Tracked Wiki source revision | `a038e7fc364c526f956ebacce7f5779fbfaac0a8` |
| Tracked `wiki-source` subtree | `25e13e4d8d3a0379e836f5ce130535464277346f` |
| Separate Wiki repository branch | `master` |
| Published Wiki commit | `79c678c5c54427facba990b394240c1567c5b75a` |
| Published Wiki tree | `25e13e4d8d3a0379e836f5ce130535464277346f` |
| Published page count | `24` |
| Live validation | passed |

Published Wiki tree equality with the tracked `wiki-source` tree is the publication-equivalence proof. The Wiki `master` branch belongs to the separate Wiki repository and is not a SharpAccess source branch.

## Current repository state versus release provenance

At the audited closure baseline, protected `main` is `a038e7fc364c526f956ebacce7f5779fbfaac0a8`. It intentionally postdates the immutable RC1 package provenance commit because documentation-only post-release Wiki synchronization was merged after publication.

Never substitute current `main` for the historical RC1 package provenance commit, and never repoint the RC1 tag to a later documentation commit.

## Evidence classification

- **Published/complete**: release, package, symbols, prerelease, public discovery, nuspec metadata, clean-consumer restore/build, Wiki publication, and license state above.
- **Continuing Supported-provider obligation**: future applicable PostgreSQL revisions still require real-engine contracts, restricted-principal, historical-upgrade, concurrency/cancellation/timeout/SQLSTATE, query-plan, coverage/mutation, recovery, package, and consumer evidence.
- **Future stable**: stable `1.0.0` requires a fresh exact-revision decision and may reuse evidence only where the then-current policy explicitly permits it.

Post-release documentation/control synchronization must not regenerate immutable RC1 PostgreSQL, OIDC, controlled-performance, package, or publication evidence solely because documentation changed.
