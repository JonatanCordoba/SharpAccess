# Quality gates

SharpAccess quality policy is organized by product behavior and support status rather than delivery phase. This document owns enforced coverage, changed-code, complexity, mutation, SAST/SCA, verification-throughput, and retained-evidence rules. `QUALITY-REPORT.md` separately owns the engineering-quality report schema and interpretation.

## Measurement principles

- Measure only what has an owner and an action.
- Keep thresholds evidence-based and ratchet them deliberately.
- Do not lower gates to make a change pass without an approved, expiring risk exception.
- Distinguish repository evidence from host production evidence.
- Do not claim certification, availability, or compliance from automated checks alone.
- Handwritten production behavior is included unless compiler-generated, genuinely generated, or excluded with a reviewed justification.

## Windows-only execution and throughput

Verification runs on Windows with PowerShell 7. The repository does not maintain Bash peers, Linux/macOS jobs, Docker, Compose, or service containers.

Release restore and build are serialized. After a successful build, the five test projects may run in bounded parallel batches with isolated result directories. Coverage normalization, merging, thresholds, changed-line analysis, complexity, mutation, packaging, package smoke, SBOM generation, quality-report generation, and final Git-state checks remain serialized.

Set `SHARPACCESS_MAX_PARALLEL_TEST_JOBS` to an integer from one through eight. The default is half of detected logical processors, clamped to one through two.

```powershell
$env:SHARPACCESS_MAX_PARALLEL_TEST_JOBS = '1'
./scripts/verify-local.ps1 -RepositoryRoot $PWD -Version '0.9.0-rc.1'
```

## Required coverage gates

`eng/CoveragePolicy.props` is the support-level policy:

- Core: at least 85% line and 75% branch;
- SQLite: at least 80% line and 65% branch;
- PostgreSQL: at least 80% line and 65% branch as a Supported provider;
- changed handwritten production code: at least 90% line and 75% branch;
- combined Supported production: retain the additional checked-in non-regression floor.

The canonical combined Supported dataset includes `SharpAccess.Core`, `SharpAccess.Sqlite`, and `SharpAccess.Postgres`. A missing required package or coverage input fails closed rather than turning an empty or narrowed dataset into a passing result.

Provider-contract classes use `Provider` traits. Capability traits identify shared behavior such as migrations, refresh tokens, one-time tokens, temporal behavior, error classification, transactions, and concurrency. Coverage attribution uses traits and explicit assembly scope rather than class-name matching.

## Changed-line coverage

`scripts/changed-line-coverage.ps1` compares changed lines and branches only within an explicit coverage scope. Pull requests use the base revision, multi-commit pushes use the pre-push revision, and manual or scheduled runs fall back to the first parent. Evidence records the revision, scope, required packages, working-tree state, and SHA-addressed artifacts.

Core or shared persistence, migration, authentication, authorization, or public-contract changes select every Supported provider. Provider-only changes select that provider plus Core. A scheduled full matrix remains required regardless of path selection.

## Complexity and CRAP ratchet

The complexity runner writes ignored evidence under `artifacts/quality/complexity`:

- `complexity.json`;
- `complexity.csv`;
- `complexity.md`.

The Supported-production ratchet scope contains Core, SQLite, and PostgreSQL. Cyclomatic complexity is read from normalized method coverage data. CRAP is calculated as:

```text
complexity² × (1 − line coverage)³ + complexity
```

A method is a hotspot when it exceeds the reviewed policy threshold. `eng/ComplexityBaseline.json` contains reviewed historical hotspots for the complete Supported scope. Normal verification fails when:

- a Supported-production method becomes a new hotspot;
- an approved hotspot gains cyclomatic complexity;
- an approved hotspot gains more than the configured CRAP tolerance;
- ratcheted policy or assembly scope changes without reviewed baseline alignment;
- the baseline contains superseded method identities or has not been explicitly approved.

Improvements do not require baseline edits. Regenerate the baseline after deliberate hotspot remediation or a reviewed Supported-scope change, never merely to accept an unrelated regression.

```powershell
./scripts/setup-test.ps1 -RepositoryRoot $PWD -UpdateComplexityBaseline
```

Baseline refresh is never invoked automatically by `verify-local`, `release-dry-run`, or `local-ci`.

Prioritize security-sensitive or frequently changed methods with high complexity and CRAP. Add characterization, concurrency, rollback, and mutation evidence before restructuring behavior. Prefer guard clauses, pure decisions, explicit classifiers, and clear transaction orchestration. Do not split code only to satisfy a number or introduce abstractions that hide provider semantics.

## Engineering-quality evidence

Complete local verification must produce `artifacts/quality-report/index.html`, `metrics.json`, `manifest.json`, and local coverage pages for the exact revision. The report covers line and branch coverage, CRAP, cyclomatic complexity, maintainability index, class coupling, afferent/efferent coupling, instability, and unified hotspots.

New aggregate maintainability and coupling thresholds remain evidence-only until their calculation, scope, exclusions, baseline, ownership, and remediation policy are approved. Existing coverage and complexity gates remain blocking.

## Mutation tiers

`eng/mutations.json` contains deterministic critical mutations mapped to stable `MutationInvariant` traits. Pull-request runs select mutations for changed security files; scheduled, Supported-provider, and release tiers run broader catalogs. Every selected critical mutation must be killed.

The runner copies the active tracked and non-ignored tree to an isolated temporary directory, validates each unmutated invariant baseline, mutates only the copy, fingerprints the primary working tree before and after execution, removes the copy in `finally`, and prunes abandoned snapshots. Revision, catalog hash, tree fingerprints, baselines, and outcomes are retained under `artifacts/mutation`.

The critical catalog covers password verification, signing-key selection, account state, tenant membership, fail-closed authorization, refresh rotation and replay, one-time-token consumption, and transaction rollback. Exact test method names are not part of mutation selection.

## Fault and concurrency contracts

Shared provider contracts include rollback after a failed registration-token insert and concurrent registration, lockout, password reset, token replacement, one-time consumption, refresh replay, global-role changes, and tenant-ownership transfer. PostgreSQL execution is optional for ordinary unconfigured local development but mandatory when its Supported-provider or release evidence is selected.

## Security and supply-chain gates

Required gates include:

- DevSkim SAST with retained SARIF;
- NuGet vulnerability audit with configured severities treated as errors;
- dependency review on pull requests;
- tracked-file secret scanning;
- native GitHub secret scanning and push protection where available;
- full-commit workflow-action pinning;
- locked restore and reviewed lock-file changes;
- deterministic package, SBOM, checksum, and provenance evidence for release revisions.

## Test ownership

- `SharpAccess.UnitTests` owns Core behavior and provider-neutral contracts.
- `SharpAccess.IntegrationTests` owns application flows against SQLite.
- `SharpAccess.EndpointTests` owns HTTP behavior, policy enforcement, smoke, and bounded endpoint-performance evidence.
- `SharpAccess.ProviderContractTests` owns provider registration, options, infrastructure, migrations, persistence security, transactions, concurrency, SQLite, and PostgreSQL contracts.
- `SharpAccess.PackageTests` owns package surface, public API, identity, topology, status, documentation, versioning, and repository policy.

Capability folders and invariant-based names are stable. Phase-named and coverage-topology test files are rejected by structure validation.

## Retained evidence

Applicable default-branch and release workflows retain SHA-addressed build, package, Supported-production coverage, changed-code coverage, provider-contract TRX, mutation, fuzz/concurrency TRX, SAST SARIF, NuGet SCA JSON, secret-scan evidence, SBOMs, quality-report outputs, performance evidence, recovery evidence, and operational-readiness artifacts. Evidence generated by scripts embeds the exact Git revision and records whether the source tree was dirty.

## Review cadence

Review gates after security incidents, failed recovery exercises, provider-status changes, major package releases, material architecture changes, repeated performance regressions, and at least annually.
