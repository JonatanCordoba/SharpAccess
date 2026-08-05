# Release evidence matrix

## Classification

- `release-candidate-required`: must pass before public `0.9.0-rc.1` publication.
- `stable-required`: must pass again before stable `1.0.0` publication.
- `environment-blocked`: a control exists but required protected infrastructure is unavailable; this is not success.
- `not-run-by-request`: an exploratory invocation omitted a control; this is not success.
- `not-applicable`: a capability does not apply, with the reason recorded.

SQL Server and MySQL are future roadmap candidates, not active release targets.
## Current development evidence status

| Control | Status | Exact identity / limitation |
|---|---|---|
| Public-surface and publication-contract integration | Complete | PR #148 integrated as `c99c6866956c06414de23a6c30129629dc4b0e47` |
| Controlled performance capture and review | Complete | Three-process median candidate captured on `c99c6866956c06414de23a6c30129629dc4b0e47` in `local-controlled` |
| Baseline-only integration | Complete | PR #149 integrated as `86ac10a479e8c6fed78bf78e9024d863127f1129`; only `eng/PerformanceBaseline.json` changed |
| Exact clean-tree development verification | Maintainer-reported complete | Selected revision `86ac10a479e8c6fed78bf78e9024d863127f1129` |
| Complete protected development matrix | Maintainer-reported complete | PostgreSQL, live OIDC, approved performance, recovery, packages, SBOMs, quality report, checksums, evidence index, and export dry run on `86ac10a479e8c6fed78bf78e9024d863127f1129` |
| GitHub-hosted PR workflows | Environment-blocked | PRs #148 and #149 were blocked before runner allocation by billing/spending limits; not passing evidence |
| Canonical-metadata lineage | In progress | Its integration creates a new exact revision and requires one final verify/capture/approval/matrix/export cycle |
| Public-root evidence | Not run | Requires separate explicit authorization before any Git operation against `JonatanCordoba/SharpAccess` |

## Active package cohort

| Evidence | Core | SQLite | PostgreSQL | Retained source |
|---|---|---|---|---|
| Authoritative version | `0.9.0-rc.1` required | synchronized | synchronized | `eng/Version.props`, package metadata |
| Windows build/test | required | required | required | Windows CI/release artifacts |
| Coverage and changed-code coverage | 85/75 plus 90/75 changed code | 80/65 | 80/65 | coverage artifacts |
| Engineering-quality report | required | required | required | `artifacts/quality-report` |
| Complexity/CRAP ratchet | current Supported baseline required | current Supported baseline required | current Supported baseline required | complexity policy/baseline and report artifacts |
| Provider contracts | provider-neutral behavior | required | non-skipped real-engine run required | provider-contract artifacts |
| Historical migrations | shared contract | fixtures required | real-engine upgrades required | migration/provider artifacts |
| Restricted principals | not applicable | Windows file/ACL guidance | required | PostgreSQL readiness artifacts |
| Recovery | package procedures | offline drill required | native logical backup/restore required | operations artifacts |
| Performance and capacity | controlled baseline required | endpoint/query profile | query-plan and controlled baseline required | performance/provider artifacts |
| Package consumer | required | required | required | package-smoke artifacts |
| Mutation evidence | signing, state, authorization, refresh invariants | reference-provider invariants | replay, one-time-token, SQLSTATE, transaction invariants | mutation artifacts |
| OIDC | protected live and deterministic protocol evidence | provider-neutral | provider-neutral | OIDC artifacts |
| SAST, SCA, secrets | required | required | required | CI/release artifacts |
| SBOM, checksums, provenance | required package archive | required package archive | required package archive | SBOM/release artifacts |
| Deterministic export | required | required | required | `artifacts/release-export` |
| Public-root revalidation | required | required | required | canonical repository workflows/artifacts |
| Post-publication smoke | required | required | required | clean consumer evidence |

## Platform and tooling evidence

All evidence is generated on Windows with PowerShell 7. The release tree contains no Bash, Docker, Compose, service-container, Linux, or macOS release paths.

PostgreSQL evidence uses protected configuration plus native Windows PostgreSQL client tools or an approved managed database. Missing service infrastructure fails required stages rather than silently skipping.

## Continuing PostgreSQL support boundary

PostgreSQL is Supported. Applicable release revisions remain unaccepted until they:

- pass every provider-specific required row above;
- retain the approved public `AddPostgresAccess` registration and package surface;
- produce synchronized runtime, symbol, XML documentation, README, SBOM, and provenance artifacts;
- pass `scripts/postgres-promotion.ps1` or the equivalent aggregate Supported-provider evidence on the exact commit;
- retain reviewer approval and artifact locations.

Prior promotion evidence does not permanently verify later changed revisions. Protected OIDC, the approved performance baseline, deterministic export, public-root verification, publication, and post-publication checks remain separate requirements.

## RC and stable reuse

`0.9.0-rc.1` evidence cannot be reused blindly for stable `1.0.0`. Stable publication requires a fresh exact-revision matrix after RC feedback and defects are dispositioned. Evidence may be referenced only when its underlying source, package, environment, and expiration policy remain unchanged and the stable policy explicitly permits reuse.

## Future roadmap providers

SQL Server and MySQL have no active evidence matrix. Reintroduction requires a new ADR and a new matrix covering native Windows real-engine contracts, migrations, restricted principals, query plans, coverage, recovery, package consumers, operations, and publication.
