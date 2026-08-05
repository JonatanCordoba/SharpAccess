## Summary

Describe the implemented scope and why it belongs in this pull request.

## Exact revision and validation

- **Head commit SHA:**
- **Expected integration strategy:** merge commit / rebase merge / squash merge
- **Expected integration revision or parent contract:**
- **Approved performance revision, if applicable:**
- **Targeted checks run before staging:**
- **Exact post-commit clean-tree command:** `./scripts/verify-local.ps1 -RepositoryRoot $PWD`
- **Post-commit result:**
- **Retained workflow runs or artifacts:**
- **Environment-blocked or intentionally skipped evidence:**

- [ ] The exact staged diff was reviewed.
- [ ] The recorded validation was run against the commit identified above.
- [ ] Tests, coverage, locked restore, package audit, endpoint smoke, package smoke, package validation, SBOMs, diagnostics, and applicable recovery evidence were not silently bypassed.
- [ ] Any required external-provider or real-engine job failed rather than silently skipping when its evidence was mandatory.
- [ ] Evidence referenced by this pull request will remain reachable after the selected integration strategy, or it is explicitly marked for post-merge recapture.
- [ ] If this pull request is squash-merged, branch-head evidence is not being represented as evidence for the resulting squash commit.

## Security, privacy, operational, and release impact

Describe the effect on authentication, authorization, token handling, tenant isolation, OAuth/OIDC, password flows, persistence, telemetry, privacy, package metadata, or release controls. Write `None` only after review.

- **Security and privacy impact:**
- **Provider impact:**
- **Public API or package impact:**
- **Compatibility impact:**
- **Operational, rollback, and recovery impact:**

- [ ] Logs, activities, metrics, artifacts, and issue content contain no secrets, raw tokens, credentials, connection strings, or production personal data.
- [ ] Provider status remains accurate and no future provider is presented as supported.
- [ ] `PROJECT_MANIFEST.md`, `docs/ROADMAP.md`, `docs/RELEASE-CANDIDATE.md`, `docs/RELEASE-EVIDENCE-MATRIX.md`, and applicable operational documents remain accurate.

## Risk and change record

- **Change classification:** standard / normal / high-risk
- **Linked change record or rationale:**
- **Exceptions, owner, compensating controls, remediation, and expiry:**

## Issue tracking

Closes #
