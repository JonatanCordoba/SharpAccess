# Integrated release-candidate evidence

## Purpose

`JonatanCordoba/SharpAccess` is the canonical implementation, review, verification, and release repository. Public NuGet packages, canonical tags, and GitHub releases originate only from its protected release flow.

The first public candidate is package version `0.9.0-rc.1` with signed tag `v0.9.0-rc.1`. Public artifacts and the canonical tag are created only from the exact verified clean root in `JonatanCordoba/SharpAccess`.

SharpAccess release evidence is Windows-only and PowerShell 7-only.
## Current development evidence

The final public-surface and publication-contract change was integrated through PR #148 as exact revision `c99c6866956c06414de23a6c30129629dc4b0e47`. The controlled three-process median candidate captured on that revision was reviewed and promoted through baseline-only PR #149, producing selected revision `86ac10a479e8c6fed78bf78e9024d863127f1129`.

The tracked baseline is approved for revision `c99c6866956c06414de23a6c30129629dc4b0e47` in reference environment `local-controlled`. The maintainer reports that exact clean-tree verification and the complete protected release-candidate matrix passed on `86ac10a479e8c6fed78bf78e9024d863127f1129`.

GitHub-hosted pull-request workflows for PRs #148 and #149 were blocked before runner allocation by account billing/spending limits. They remain classified as blocked, not passing, and no product or test execution occurred on those hosted jobs.

The canonical-metadata synchronization creates a later revision. That later lineage requires one final exact clean-tree verification, controlled candidate, baseline-only approval, protected matrix, and deterministic export before a public root may be created.

## Authoritative version

`eng/Version.props` owns the synchronized version for Core, SQLite, and PostgreSQL. The release-candidate command rejects a requested version that differs from that file.

## Complete candidate invocation

Configure the approved PostgreSQL scratch database, reset acknowledgement, readiness opt-in, native PostgreSQL client tools, protected OIDC values, and the controlled Windows reference environment before invoking:

```powershell
./scripts/release-candidate.ps1 `
  -RepositoryRoot $PWD `
  -Version '0.9.0-rc.1' `
  -ReferenceEnvironment 'local-controlled' `
  -RequirePostgres `
  -RequireOidcLiveEvidence `
  -RequireApprovedPerformanceBaseline
```

PostgreSQL evidence is mandatory regardless of whether the retained compatibility switch `-RequirePostgres` is supplied. The switch remains accepted so existing protected invocations do not break.

The complete run performs:

1. the exact clean-tree Windows local gate;
2. Core, SQLite, PostgreSQL, combined-supported, and changed-code coverage;
3. the exact-revision HTML engineering-quality report;
4. the approved controlled Windows performance ratchet;
5. PostgreSQL real-engine provider contracts;
6. PostgreSQL Supported-provider coverage and mutations;
7. PostgreSQL native recovery;
8. protected OIDC live smoke;
9. deterministic tracked-file export dry run;
10. checksums and the revision-bound evidence index.

A required stage fails rather than silently skipping. Infrastructure-blocked, missing, expired, or not-run evidence is not passing evidence.

When approved performance evidence is requested, orchestration validates the baseline status, exact approved revision ancestry, and controlled-environment label before running the expensive release stages. A squash-orphaned approval fails immediately.

## Exploratory invocation

Maintainers may omit protected switches while developing orchestration. Such output is written with status `incomplete` and cannot authorize an RC tag or publication.

```powershell
./scripts/release-candidate.ps1 `
  -RepositoryRoot $PWD `
  -Version '0.9.0-rc.1' `
  -ReferenceEnvironment 'local-controlled'
```

The evidence index records omitted protected stages as `not-run-by-request`. Do not describe this invocation as release-ready.

## Performance approval sequence

Performance approval is exact-revision evidence, not merely tree evidence.

1. Merge all implementation and release-gate changes.
2. Verify the exact resulting protected `master` revision.
3. Capture the controlled candidate on that exact revision.
4. Review the complete metric catalog and environment fingerprint.
5. Create a baseline-only pull request based directly on the approved revision.
6. Merge in a way that preserves the approved revision as an ancestor.
7. Run the complete protected release-candidate command on the resulting exact revision.

A feature-branch approval does not survive a squash merge when the approved commit is no longer reachable from the merged revision. Evidence-referenced branches must remain available until the approval is preserved in protected history or explicitly invalidated and replaced.

## Dependency assurance

The pull-request dependency workflow first attempts GitHub dependency review. When that hosted capability is unavailable, it runs a locked NuGet vulnerability audit with audit mode `all` and warnings as errors.

The NuGet fallback is a compensating control, not native GitHub dependency-review evidence. The exception, owner, remediation condition, and expiry must remain visible until hosted dependency review is enabled or the governing policy is formally changed.

## Evidence outputs

| Output | Purpose |
|---|---|
| `artifacts/release-candidate/evidence-index.json` | schema, exact revision, package version, Windows platform, active cohort, stage classification, and status |
| `artifacts/release-candidate/SHA256SUMS` | deterministic checksum inventory |
| `artifacts/release-candidate/checksums.json` | machine-readable checksum evidence |
| `artifacts/quality-report/index.html` | offline consolidated engineering-quality report |
| `artifacts/quality-report/metrics.json` | machine-readable coverage, CRAP, complexity, maintainability, and coupling data |
| `artifacts/quality-report/manifest.json` | exact-revision report inputs, versions, and hashes |
| `artifacts/performance/release-candidate` | controlled performance and approved-baseline evidence |
| `artifacts/release-export` | tracked-file archive, manifests, tree equivalence, and archive hash |
| `artifacts/packages` | prerelease runtime and symbol packages |
| `artifacts/sbom` | deterministic CycloneDX 1.6 and SPDX 2.3 evidence |
| `artifacts/operations` | diagnostics, SQLite/PostgreSQL recovery, and protected OIDC evidence |
| `artifacts/provider-coverage/postgres` | PostgreSQL Supported-provider coverage evidence |
| `artifacts/postgres-promotion/evidence.json` | historical-name aggregate PostgreSQL evidence when the dedicated command is run |

Evidence must not contain credentials, raw tokens, authorization codes, connection strings, account identifiers, machine usernames, absolute workstation paths, or unbounded caller-controlled values.

## Workflow

`.github/workflows/release-candidate.yml` is manually dispatched on `windows-latest`. It:

- defaults to `0.9.0-rc.1`;
- defaults the controlled environment to `controlled-windows-runner-01`;
- installs native PostgreSQL client tools;
- consumes protected PostgreSQL and OIDC configuration;
- requires the approved performance baseline by default;
- runs the integrated PowerShell release-candidate entry point;
- retains exact-revision evidence for review.

It does not use Linux/macOS runners, Bash, Docker, Compose, or service containers.

SQL Server and MySQL are not active release-candidate targets. They remain roadmap candidates only.

## Completion boundary

Release-candidate evidence is complete only when:

1. the final version, implementation, metadata, and release-control changes are merged;
2. the exact resulting `master` commit passes the complete Windows clean-tree gate;
3. the current complexity baseline covers Core, SQLite, and PostgreSQL and contains no superseded hotspot identities;
4. PostgreSQL contracts, coverage, mutation evidence, query plans, restricted-principal evidence, and native recovery pass;
5. protected OIDC evidence passes;
6. the controlled performance baseline is reviewed and approved on a revision reachable from the selected release revision;
7. the approved baseline environment matches the invoked controlled environment;
8. no unresolved Critical, High, or release-blocking Moderate finding remains;
9. the candidate SHA, tree, package version, and retained artifact locations are approved;
10. canonical metadata is committed and reverified before deterministic export;
11. the exact clean public root passes the complete release matrix;
12. no public package, canonical tag, or public GitHub release is created outside the protected SharpAccess release flow.
