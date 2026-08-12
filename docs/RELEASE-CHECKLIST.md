# Release checklist

This checklist owns future stable-release completion. The published RC1 identities are retained in `docs/RELEASE-EVIDENCE-MATRIX.md`; they are not re-opened as unchecked work here.

## Current release state

- [x] `0.9.0-rc.1` is published from immutable commit `4595545d8afd84c58795fc02c2c242533cdff1ac`.
- [x] The signed tag is `v0.9.0-rc.1`.
- [x] Core, SQLite, and PostgreSQL runtime and symbol packages are published.
- [x] Public discovery, published nuspec metadata, and clean-consumer restore/build passed.
- [x] GitHub prerelease publication completed.
- [x] The live 24-page Wiki matches tracked `wiki-source` content.
- [x] MIT is the canonical package license state.
- [x] `main` is the canonical protected SharpAccess source branch.

Post-release `main` commits do not change RC1 package provenance and do not require immutable RC1 provider/OIDC/performance/publication evidence to be rerun.

## Future stable `1.0.0`

Stable work is not active until explicitly opened. When it is opened, require a fresh selected stable revision and the then-current evidence policy.

- [ ] RC feedback and defects are dispositioned.
- [ ] The exact stable revision and tree are recorded.
- [ ] `eng/Version.props` contains the reviewed stable version.
- [ ] Core, SQLite, and PostgreSQL remain the reviewed active cohort or an explicitly approved new cohort is documented.
- [ ] Exact clean-tree Windows verification passes.
- [ ] Applicable Supported-provider contracts, coverage, mutation, recovery, and operational evidence pass.
- [ ] PostgreSQL real-engine evidence passes when PostgreSQL remains Supported.
- [ ] Protected OIDC and controlled performance evidence satisfy the stable policy.
- [ ] Package metadata, public API, migrations, compatibility, security, operations, SBOMs, checksums, provenance, and consumer validation pass.
- [ ] No required evidence is classified as success when failed, skipped, blocked, missing, expired, or not run.
- [ ] The stable tag is created only from the exact approved stable revision.
- [ ] Publication consumes the validated package bytes through the protected SharpAccess publication workflow.
- [ ] Post-publication clean-consumer validation passes.
- [ ] Final immutable stable identities are recorded in the release evidence ledger.

Completion of RC1/migration stage closure does not authorize stable tagging or publication.
