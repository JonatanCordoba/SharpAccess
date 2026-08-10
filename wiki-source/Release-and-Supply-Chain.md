# Release and Supply Chain

SharpAccess release evidence is exact-revision, Windows-only, and fail-closed.

## Release candidate matrix

The protected RC command coordinates:

- clean locked restore and build;
- test suites and coverage;
- changed-line and complexity gates;
- critical mutation invariants;
- SAST and dependency checks;
- provider contracts and provider coverage;
- SQLite and PostgreSQL recovery evidence;
- controlled performance evidence and approved baseline;
- fresh protected OIDC live evidence;
- deterministic package/export checks;
- SBOMs, checksums, provenance, and release evidence.

## Publication integrity

The signed tag, packages, symbols, checksums, SBOMs, provenance, and GitHub prerelease must all originate from the same exact verified revision.

## NuGet Trusted Publishing

`0.9.0-rc.1` was published through NuGet Trusted Publishing using the protected `publish-nuget.yml` workflow and the `nuget-release` GitHub environment. The workflow re-verifies repository identity, the signed tag, the exact release-candidate run and immutable artifact, package metadata and checksums, and unpublished-version state before exchanging GitHub OIDC for a short-lived NuGet credential.

Publication order:

1. `SharpAccess.Core`
2. `SharpAccess.Sqlite`
3. `SharpAccess.Postgres`

Do not use `--skip-duplicate` for the first canonical release.

## Public verification

Public hosted verification must complete on the exact release revision before final RC publication. A skipped, cancelled, timed-out, neutral, action-required, or infrastructure-blocked job is not passing evidence. A narrowly reviewed outage exception may allow unrelated workflow-only maintenance to merge, but it must never be reused as release evidence.

The disambiguated check identities include `ci-windows`, `operational-readiness-windows`, `provider-contracts-classify`, and `test-scope-classify`. Provider and integrated release evidence also requires successful applicable jobs such as `sqlite-supported`, `postgres-native`, and `windows-release-candidate`, with retained artifacts reviewed for the exact revision.

Wiki publication, Trusted Publishing, signed tagging, package publication, GitHub prerelease creation, and clean-consumer validation are separate release gates. For `0.9.0-rc.1`, the protected hosted release candidate, Trusted Publishing, signed tag, package and symbol publication, GitHub prerelease, and clean-consumer validation completed successfully.

## References

- [Release candidate](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/RELEASE-CANDIDATE.md)
- [Release checklist](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/RELEASE-CHECKLIST.md)
- [Release evidence matrix](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/RELEASE-EVIDENCE-MATRIX.md)
- [Supply chain](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/SUPPLY-CHAIN.md)
- [NuGet package publication](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/NUGET-PACKAGE.md)
