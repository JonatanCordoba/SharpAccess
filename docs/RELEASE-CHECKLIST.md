# Release checklist

A checked item represents retained evidence, not intention. Infrastructure-blocked, skipped, expired, or unavailable evidence is not passing evidence.

## Release identity

- [ ] `eng/Version.props` records the reviewed synchronized package version.
- [ ] The first public candidate uses package version `0.9.0-rc.1` and signed tag `v0.9.0-rc.1`.
- [ ] The final development revision and tree are recorded as full immutable SHAs.
- [ ] The final clean public-root revision and tree are recorded as full immutable SHAs.
- [ ] Branch-head, integration, squash-merge, development-master, and public-root evidence are not conflated.
- [ ] Public API, compatibility, provider, security, operator, and package impact are documented.
- [ ] Core, SQLite, and PostgreSQL are the only active package targets.
- [ ] SQL Server and MySQL remain roadmap-only and absent from source/package output.
- [ ] No unresolved or expired risk exception applies.

## Windows verification in the development repository

- [ ] `./scripts/postgres-promotion.ps1 -RepositoryRoot $PWD` passed as continuing PostgreSQL release evidence against the applicable exact committed revision.
- [ ] `./scripts/verify-local.ps1 -RepositoryRoot $PWD -Version '0.9.0-rc.1'` passed after the final committed version and metadata transition.
- [ ] The exact resulting `master` commit passed complete verification after squash merge.
- [ ] The working tree remained clean after verification.
- [ ] Windows CI, SAST, dependency review, tracked-secret scan, and operational readiness passed.
- [ ] The SAST SARIF artifact contains no unresolved blocking finding.
- [ ] Core, SQLite, PostgreSQL, combined-supported, and changed-code coverage passed.
- [ ] The exact-revision HTML quality report and its JSON/manifest evidence were generated.
- [ ] The reviewed complexity baseline reflects current post-remediation methods and the complete Supported cohort.
- [ ] Required mutation, fuzz, concurrency, diagnostics, and recovery evidence passed.
- [ ] PostgreSQL real-engine contracts, readiness, historical upgrades, query plans, PostgreSQL-specific mutations, and native recovery passed.
- [ ] Protected OIDC evidence passed.
- [ ] `eng/PerformanceBaseline.json` contains an approved controlled Windows baseline rather than `pending-reference-run`.
- [ ] Package-consumer smoke validation passed for Core, SQLite, and PostgreSQL.
- [ ] No Bash, Docker, Compose, service-container, Linux, or macOS release path remains.

## Clean public repository bootstrap

- [ ] `JonatanCordoba/SharpAccess` exists as a new empty repository with no generated files or imported history before bootstrap.
- [ ] Canonical repository URLs, Source Link, badges, security links, SBOM identity, and provenance configuration are committed and verified in SharpAccess.
- [ ] `SharpAccess` was not created by rename, mirror, fork, or history rewrite.
- [ ] The approved revision was exported using tracked files only.
- [ ] The export excluded `.git`, refs, local artifacts, secrets, caches, test databases, internal prompts, audits, and unpublished evidence.
- [ ] Development SHA, development tree, deterministic manifest, and archive checksum were recorded.
- [ ] A normalized manifest comparison proves the export matches the approved tree before push.
- [ ] The public repository begins with one signed root commit and no inherited commits, branches, tags, notes, replace refs, or pull-request refs.
- [ ] The public root SHA and normalized manifest were recorded and match the approved tree.
- [ ] No source or metadata file was edited only in staging; every change was committed on a SharpAccess branch and revalidated.
- [ ] Branch protection, required checks, CODEOWNERS, security reporting, Dependabot, secret scanning, and publication environments are configured.
- [ ] Complete Windows release verification was rerun from the exact clean root commit.

## Supply chain and packages

- [ ] NuGet lock files are current and locked restore passed.
- [ ] Packable projects generate XML documentation and include the package README.
- [ ] Exact Core, SQLite, and PostgreSQL runtime and symbol packages were generated from the clean root.
- [ ] Package IDs, versions, descriptions, dependencies, symbols, license, README, repository URLs, and Source Link were inspected.
- [ ] Deterministic CycloneDX 1.6 and SPDX 2.3 documents were generated for all three packages.
- [ ] `sbom-evidence.json` records canonical identity, publication mode, root hashes, and byte-reproducible output hashes.
- [ ] Package, source archive, export manifest, and SBOM checksums were generated.
- [ ] Provenance identifies the clean root and approved development revision.
- [ ] Every external workflow action remains pinned to a full commit SHA.

## Operations and security

- [ ] Operational-readiness evidence exists for the release revision.
- [ ] SQLite and PostgreSQL recovery evidence is current.
- [ ] Rollback, signing-key rotation, database recovery, and incident ownership are current.
- [ ] At least one reviewed production asymmetric-signing recipe is tested and documented.
- [ ] Monitoring, alerts, logs, audit retention, backups, and restore procedures are approved.
- [ ] Privacy and data-retention responsibilities are reviewed.

## `0.9.0-rc.1` publication

- [ ] Output contains only `SharpAccess.Core`, `SharpAccess.Sqlite`, and `SharpAccess.Postgres` version `0.9.0-rc.1`.
- [ ] Public packages are generated and published only from the verified clean root.
- [ ] Release notes describe security-relevant, compatibility-relevant, and operator-relevant changes.
- [ ] The signed `v0.9.0-rc.1` tag is created in `JonatanCordoba/SharpAccess`.
- [ ] The GitHub release and NuGet packages are marked prerelease.
- [ ] Post-publication installation and authentication smoke tests pass from a clean consumer project.

## Stable `1.0.0`

- [ ] RC feedback and defects are dispositioned.
- [ ] Every stable gate is rerun for the exact stable revision.
- [ ] The signed `v1.0.0` tag and stable GitHub release are created in `JonatanCordoba/SharpAccess`.
- [ ] Advisory publication is coordinated when applicable.

Do not call the release ready when required evidence was not run, failed, expired, or is unavailable.
