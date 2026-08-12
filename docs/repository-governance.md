# Repository governance

## Canonical repository and branches

`JonatanCordoba/SharpAccess` is the canonical source, development, review, verification, release, and publication repository.

The canonical protected source/default branch is `main`. Ordinary source workflows target `main`; `master` is not a SharpAccess source branch. The separate GitHub Wiki repository uses its own `master` branch and must not be conflated with source governance.

`JonatanCordoba/dotnet-auth` is historical migration evidence only and is not an active development or release authority.

## Pull-request protection

The verified protected `main` pull-request policy uses these nine checks:

1. `Validate pull request evidence`
2. `sqlite-supported`
3. `ci-windows`
4. `devskim`
5. `operational-readiness-windows`
6. `provider-contracts-classify`
7. `review`
8. `test-scope-classify`
9. `tracked-secret-scan`

`postgres-native` and the integrated release-candidate job are exact-main/release evidence, not universal pull-request checks.

Repository settings and `.github/required-checks.json` must remain synchronized. Drift is a governance failure, not permission to weaken the live policy.

## Change policy

Use a coherent branch and pull request for non-emergency changes. Review working and staged diffs, commit intentionally, run the required exact-commit verification, and preserve protected history. Revert mistakes on `main` with a new commit rather than rewriting protected history.

When evidence depends on ancestry, choose the integration strategy before relying on a branch revision. Squash merging is appropriate for ordinary work only when no retained evidence requires the feature revision to remain an ancestor.

## Release governance

RC1 `0.9.0-rc.1` is already published. Its immutable package provenance commit is `4595545d8afd84c58795fc02c2c242533cdff1ac`; current `main` may advance without changing that provenance.

Future tagging and publication require explicit authorization and must use the protected SharpAccess release flow. Stable `1.0.0` has not started as an execution stage.

## Settings audit

Periodically verify:

- [ ] `main` is the repository default branch.
- [ ] `main` has branch protection/ruleset enforcement.
- [ ] required review/conversation-resolution policy matches the approved governance contract.
- [ ] the nine required PR checks above match the live protected policy and `.github/required-checks.json`.
- [ ] force pushes and branch deletion are disabled for protected `main`.
- [ ] CODEOWNERS, private vulnerability reporting, dependency controls, secret scanning/push protection, and protected release environments remain configured.
- [ ] release workflows retain least privilege and exact-artifact/revision checks.

Repository-setting evidence is separate from tracked-file evidence and must be inspected as settings state when a decision depends on it.
