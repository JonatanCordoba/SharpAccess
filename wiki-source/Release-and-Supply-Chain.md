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

The intended workflow uses a GitHub environment and short-lived NuGet credential. It must verify repository identity, tag identity, tag signature, locked restore, package contents, and publication order.

Publication order:

1. `SharpAccess.Core`
2. `SharpAccess.Sqlite`
3. `SharpAccess.Postgres`

Do not use `--skip-duplicate` for the first canonical release.

## Public verification

Hosted checks blocked by private quota are environment-blocked evidence, not passing evidence. Actual public hosted verification is required before final RC publication.

## References

- [Release candidate](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/RELEASE-CANDIDATE.md)
- [Release checklist](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/RELEASE-CHECKLIST.md)
- [Release evidence matrix](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/RELEASE-EVIDENCE-MATRIX.md)
- [Supply chain](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/SUPPLY-CHAIN.md)
- [NuGet package publication](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/NUGET-PACKAGE.md)
