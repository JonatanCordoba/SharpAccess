# Security and CI hardening

Phase 3 adds independent SAST, dependency review, secret scanning, action pinning, locked dependency resolution, advanced testing, formal SBOM generation, and release attestations. Phase 7 strengthens that baseline with deterministic per-package dependency graphs, offline official-schema validation, centralized service-image and action pins, and SHA-addressed retained evidence as detailed in [SUPPLY-CHAIN.md](SUPPLY-CHAIN.md).

## Gates

- Microsoft DevSkim analyzes the repository as the current independent SAST gate.
- DevSkim is pinned as a local .NET tool and blocks Critical, Important, and Moderate findings with High or Medium confidence.
- The SAST workflow has read-only repository permissions and retains SARIF output as a workflow artifact.
- GitHub-native CodeQL remains an optional enhancement when repository visibility and GitHub Code Security availability permit it.
- Dependency review rejects newly introduced moderate-or-higher vulnerable dependencies.
- Gitleaks scans full Git history with narrow allowlists for deterministic test fixtures.
- Every external GitHub Action reference is pinned to a full 40-character commit SHA and verified by paired scripts plus package tests.
- CI and release workflows use `dotnet restore --locked-mode`; lock updates are deliberate through `scripts/refresh-lock-files.*`.
- Provider coverage is enforced independently using `eng/ProviderCoverage.props` and is ratcheted upward as each peer provider matures.
- Server-provider incubation branch floors are nonzero observed ratchets, may only increase, and do not imply provider promotion.
- Weekly mutation, malformed-input fuzz, and concurrency tests complement the always-on unit, integration, endpoint, package, and provider suites.

The DevSkim replacement preserves a blocking SAST control without claiming GitHub code-scanning evidence that the current private-repository plan cannot produce. Do not remove the gate without an approved equivalent or stronger replacement.
