# Quality and Metrics

SharpAccess retains exact-revision engineering evidence for the supported production scope: `SharpAccess.Core`, `SharpAccess.Sqlite`, and `SharpAccess.Postgres`.

The block below is generated from the same `artifacts/quality-report/metrics.json` source as the root README. Do not edit the metric values manually.

<!-- SHARPACCESS_QUALITY_SNAPSHOT_START -->
**Source:** exact-revision `artifacts/quality-report/metrics.json` · **Schema:** 2 · **Enforcement:** EvidenceOnly

| Metric | p95 | Worst observed | Aggregate / release interpretation |
|---|---:|---:|---|
| Line coverage | 100.00% | 0.00% minimum | 91.39% repository aggregate |
| Branch coverage | 100.00% | 0.00% minimum | 81.99% repository aggregate |
| CRAP score | 12.14 | 25.48 maximum | Executable methods only |
| Cyclomatic complexity | 8 | 24 maximum | Roslyn source metrics |
| Maintainability index | 100 | 41 minimum | Higher is better |
| Class coupling | 15 | 49 maximum | Distinct referenced types |
| Afferent coupling (Ca) | 11.6 | 13 maximum | Project + namespace units |
| Efferent coupling (Ce) | 8 | 9 maximum | Project + namespace units |
| Instability | 1.000 | 1.000 maximum | `Ce / (Ca + Ce)`; informational |
| Critical mutation invariants | N/A | **0 survived; 0 infrastructure failures required** | Binary per selected invariant; release tier must pass |

<details>
<summary>Scope and percentile notes</summary>

- Coverage percentiles are calculated across matched executable production members in `SharpAccess.Core`, `SharpAccess.Sqlite`, and `SharpAccess.Postgres`.
- The repository aggregate remains the release coverage score; the minimum exposes the worst observed member because coverage is higher-is-better.
- CRAP, cyclomatic complexity, maintainability index, and class-coupling statistics use the report's exact member dataset.
- Ca, Ce, and instability are calculated across project and namespace dependency units.
- Mutation invariants are binary and therefore do not have a meaningful p95.
- Consult `artifacts/quality-report/index.html` and `metrics.json` for complete project, namespace, type, member, dependency, and hotspot detail.

</details>
<!-- SHARPACCESS_QUALITY_SNAPSHOT_END -->

## Blocking quality gates

- Core coverage: at least 85% line and 75% branch.
- SQLite coverage: at least 80% line and 65% branch.
- PostgreSQL coverage: at least 80% line and 65% branch while Supported.
- Changed handwritten production code: at least 90% line and 75% branch.
- Supported combined coverage retains an additional checked-in non-regression floor.
- New complexity/CRAP hotspots are rejected.
- Existing reviewed hotspots may not gain cyclomatic complexity or exceed the configured CRAP tolerance.
- Every selected critical mutation must be killed.
- Missing required evidence fails closed.

## Report outputs

Complete local verification produces:

```text
artifacts/quality-report/index.html
artifacts/quality-report/metrics.json
artifacts/quality-report/manifest.json
```

The report includes project, namespace, type, member, dependency, and hotspot views.

## Interpretation

Coverage and maintainability are higher-is-better, so the worst observed member is the minimum. CRAP, cyclomatic complexity, coupling, and instability are adverse metrics, so the worst observed value is the maximum. Mutation invariants are binary and do not have a meaningful percentile.

## Commands

```powershell
./scripts/verify-local.ps1 -RepositoryRoot $PWD -Version '0.9.0-rc.1'
./scripts/quality-report.ps1 -RepositoryRoot $PWD
./scripts/complexity-report.ps1 -RepositoryRoot $PWD
./scripts/mutation-test.ps1 -RepositoryRoot $PWD
```

Use the exact repository-supported parameters for the selected workflow.

## References

- [Quality gates](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/QUALITY-GATES.md)
- [Quality report](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/QUALITY-REPORT.md)
- [Coverage and test gates](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/COVERAGE-AND-TEST-GATES.md)
- [Verification and complexity](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/VERIFICATION-AND-COMPLEXITY.md)
