# Roadmap

## Current implementation status

The authoritative active status source is `eng/ProviderStatus.props`; the synchronized package version is owned by `eng/Version.props`.

- `SharpAccess.Core`: **Supported**.
- `SharpAccess.Sqlite`: **Supported**.
- `SharpAccess.Postgres`: **Supported** with continuing real-engine, recovery, coverage, mutation, package, and operational evidence obligations.

The repository is **Windows-only** for engineering, CI, verification, release, and supported deployment. Automation is implemented in **PowerShell 7** only. Bash, Docker, Compose, service-container, SQL Server, and MySQL implementation files are not part of the active tree.

The final RC1 public surface and publication contract were integrated through PR #148 as `c99c6866956c06414de23a6c30129629dc4b0e47`. A controlled three-process median performance candidate was captured and reviewed on that exact revision. Baseline-only PR #149 was then integrated as `86ac10a479e8c6fed78bf78e9024d863127f1129`, preserving `c99c6866956c06414de23a6c30129629dc4b0e47` as its direct parent and changing only `eng/PerformanceBaseline.json`.

The maintainer reports that exact clean-tree verification and the complete protected development-repository release-candidate matrix passed on `86ac10a479e8c6fed78bf78e9024d863127f1129`. The retained run covered PostgreSQL real-engine contracts, Supported-provider coverage and recovery; protected live Google-compatible OIDC; approved performance comparison in environment `local-controlled`; SQLite recovery; package and consumer validation; deterministic SBOMs; the engineering-quality report; checksums; the evidence index; and deterministic export dry-run evidence. GitHub-hosted pull-request workflows were blocked before runner allocation by the account billing/spending-limit state and are not represented as passing.

This canonical-metadata synchronization is documentation-only, but its integration will create a new selected development revision. Exact-revision policy therefore requires one final clean-tree verification, controlled performance capture, baseline-only approval, protected matrix, and deterministic tracked-file export on the resulting lineage. These documents intentionally describe that final cycle generically so no additional status-only commit is required afterward.

## Status vocabulary

- **Implementation complete and merged**: planned repository changes are present on `main`.
- **Evidence complete**: the exact selected commit has the required retained evidence and every referenced approval revision is reachable from protected history.
- **Release-candidate ready**: implementation, exact-revision local and protected evidence, baselines, clean public root, packages, and publication controls are complete for the candidate.
- **Stable ready**: RC feedback is dispositioned and every stable gate passes again on the exact stable revision.
- **Planned**: implementation has not been merged.

A merged pull request or configured workflow does not establish evidence completion by itself. Green branch-head workflows and tree-equivalent squash commits remain evidence for different revision identities unless the applicable policy explicitly permits otherwise.

## `0.9.0-rc.1` critical path

1. **Architecture and security foundation.** Complete.
2. **Core, SQLite, and PostgreSQL Supported implementation.** Complete; continuing provider evidence remains mandatory.
3. **Windows-only repository simplification.** Complete.
4. **Exact-revision engineering-quality report.** Complete and integrated.
5. **Critical hotspot remediation.** Authentication, JWT, OIDC, cursor, configuration, SQLite refresh rotation, PostgreSQL authorization/rotation, and quality-report hotspots have been remediated and ratcheted.
6. **RC metadata, package documentation, and public API XML documentation.** Complete.
7. **Supported-production complexity ratchet.** Complete for Core, SQLite, and PostgreSQL.
8. **Post-merge release-evidence repair.** Complete through PR #137 and the subsequent OIDC release-orchestration repairs in PRs #139–#141.
9. **Controlled Windows performance baseline.** Complete for the current selected lineage: candidate captured on `c99c6866956c06414de23a6c30129629dc4b0e47` and promoted through baseline-only PR #149 as `86ac10a479e8c6fed78bf78e9024d863127f1129`. The canonical-metadata integration requires one final recapture on its resulting exact revision.
10. **Exact merged-revision verification.** Complete on `86ac10a479e8c6fed78bf78e9024d863127f1129`; required again on the canonical-metadata merge commit and its final baseline-only child.
11. **Protected release evidence.** Maintainer-reported complete on `86ac10a479e8c6fed78bf78e9024d863127f1129`, including PostgreSQL, protected OIDC, recovery, approved performance comparison, package, SBOM, quality-report, checksums, evidence-index, and export stages. The matrix must run once more after the final baseline integration.
12. **Hosted dependency and pull-request evidence.** GitHub-hosted workflows for PRs #148 and #149 were blocked before runner allocation by billing/spending limits and are not passing evidence. The locked warnings-as-errors NuGet audit remains a compensating control and is not equivalent to native GitHub dependency review.
13. **Canonical metadata synchronization.** In progress on `release/0.9.0-rc.1-canonical-metadata`; merging it intentionally triggers the final exact-revision cycle described above.
14. **Deterministic development export.** Pending from the final verified baseline-only development lineage. The export must retain the development commit SHA, source tree SHA, normalized manifest, archive checksum, exported tree identity, operator, reviewer, and evidence locations.
15. **Clean public repository bootstrap.** Pending separate explicit authorization naming `JonatanCordoba/SharpAccess` and the bounded Git sequence. No public-repository Git operation is authorized by completion of this branch or export.
16. **Public-root security and verification.** Pending signed one-commit root, tree-equivalence proof, visibility change, branch protection, CODEOWNERS, required checks, dependency and secret controls, vulnerability reporting, trusted publication configuration, and the complete exact public-root release matrix.
17. **Public RC publication.** Pending separate explicit publication authorization, signed `v0.9.0-rc.1`, prerelease NuGet publication in dependency order, GitHub prerelease creation, and post-publication smoke from the verified clean public root. NuGet publication must run only through `.github/workflows/publish-nuget.yml`, consuming the exact validated release-candidate artifact through the protected `nuget-release` environment and NuGet Trusted Publishing with GitHub OIDC; the publication workflow does not rebuild or repack the release cohort.
18. **Stable `1.0.0`.** Planned only after RC feedback is dispositioned and a fresh stable evidence matrix passes.

## Performance approval lifecycle

A controlled candidate is captured on a clean exact revision. Approval is valid only while the approved revision is the selected release revision or an ancestor whose later changes are limited to the tracked baseline file. A feature-branch approval does not survive a squash merge merely because the resulting tree is equivalent.

The required sequence is:

1. merge implementation, gate, or final release-state changes;
2. verify the exact resulting `main` revision;
3. capture and review the controlled candidate on that revision;
4. submit a baseline-only pull request based directly on the approved revision;
5. merge without rewriting away the approved parent;
6. run the complete protected release-candidate command on the resulting exact revision.

The current selected lineage completed this sequence with approved revision `c99c6866956c06414de23a6c30129629dc4b0e47` and baseline integration revision `86ac10a479e8c6fed78bf78e9024d863127f1129`. The canonical-metadata integration changes the selected revision and therefore starts one final exact-revision cycle before deterministic export. After that cycle, the export manifest—not another documentation-only commit—records the exact final development commit and tree.

Do not delete a branch that contains a referenced evidence revision until that evidence is either retained in protected history or explicitly invalidated and replaced.

## Future provider roadmap

SQL Server and MySQL remain future roadmap candidates for development awareness only. They are not active projects, dependencies, package IDs, namespaces, public registrations, workflows, scripts, tests, operational runbooks, or release blockers.

A future proposal to reintroduce either provider requires a new accepted ADR and a separately reviewed implementation plan covering:

- provider-neutral compatibility;
- native Windows database support;
- migrations and historical upgrades;
- transaction and concurrency behavior;
- error classification;
- restricted-principal operation;
- query-plan evidence;
- coverage and mutation evidence;
- backup and recovery;
- package-consumer validation;
- security and operational documentation;
- synchronized release and rollback policy.

No placeholder source or dormant dependency may be added before that proposal is accepted.

## Out of RC1 and stable 1.0 unless separately approved

Redis, passkeys/WebAuthn, additional identity-provider presets, distributed rate limiting implementations, additional databases, vendor-specific managed-key packages, and optional observability exporters remain uncommitted roadmap ideas unless separately designed, implemented, tested, documented, and approved.
