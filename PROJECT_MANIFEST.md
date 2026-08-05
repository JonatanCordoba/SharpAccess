# Project manifest

## Runtime, platform, and package targets

- Target framework: `net10.0`.
- SDK: the exact version selected by `global.json`.
- Supported engineering, CI, release, and deployment platform: Windows.
- Repository automation: PowerShell 7 only.
- Container policy: no Dockerfiles, Compose files, service containers, or local container orchestration.
- License: MIT.
- Private development repository: `JonatanCordoba/dotnet-auth`.
- Canonical public release repository: `JonatanCordoba/SharpAccess`.
- Authoritative synchronized package version: `eng/Version.props`.
- Current release-candidate version: `0.9.0-rc.1`.

## Active package cohort

- `SharpAccess.Core`: Supported.
- `SharpAccess.Sqlite`: Supported.
- `SharpAccess.Postgres`: Supported server provider.
- Provider status source of truth: `eng/ProviderStatus.props`.

SQL Server and MySQL are absent from the active repository tree. They remain future roadmap candidates only and may return through separate architecture, implementation, compatibility, security, migration, operational, and release-evidence work.

Only projects with authoritative `Supported` status are packable through ordinary development paths. Public release-candidate and stable publication remains forbidden from `JonatanCordoba/dotnet-auth`.

## Project layout

- `src/SharpAccess.Core`: provider-neutral package source.
- `providers/SharpAccess.Sqlite`: supported SQLite provider source.
- `providers/SharpAccess.Postgres`: supported PostgreSQL provider source.
- `providers/Shared`: linked internal registration source shared by active providers.
- `samples/SharpAccess.SampleApi`: thin Minimal API and test-console host.
- `tools/SharpAccess.TestBootstrap`: deterministic test bootstrap.
- `tools/SharpAccess.MigrationTool`: migration command-line utility.
- `tools/SharpAccess.Sbom`: deterministic active-cohort SBOM generator.
- `tools/SharpAccess.QualityReport`: exact-revision engineering-quality report generator.
- `tests/SharpAccess.UnitTests`: unit tests.
- `tests/SharpAccess.IntegrationTests`: integration tests.
- `tests/SharpAccess.EndpointTests`: endpoint behavior tests.
- `tests/SharpAccess.ProviderContractTests`: SQLite and PostgreSQL provider contracts.
- `tests/SharpAccess.PackageTests`: package, public-surface, and repository-policy tests.
- `scripts`: PowerShell 7 verification and release tools.
- `.github/workflows`: Windows-only GitHub Actions workflows.
- `docs`: consumer, operator, security, quality, and release documentation.

Every active project inherits lock-file generation from `Directory.Build.props`. Project-level lock-file opt-outs are forbidden.

## Release policy

The public `0.9.0-rc.1` and initial stable `1.0.0` cohorts are Core, SQLite, and PostgreSQL. PostgreSQL remains subject to continuing real-engine contracts, restricted-principal, migration, query-plan, coverage, mutation, recovery, package-validation, and consumer evidence on applicable release revisions.

The public release repository starts from one signed root commit containing the exact approved tracked tree. No development history, branches, tags, notes, replace refs, pull-request refs, local artifacts, internal prompts, audits, or unpublished evidence are inherited.

The first canonical public tag is `v0.9.0-rc.1`. Stable `v1.0.0` is created only after RC feedback is dispositioned and the stable release matrix passes again.

## Provider-test safety

PostgreSQL contract and recovery tests require:

- a dedicated `sharpaccess_contract_tests` scratch database;
- `SHARPACCESS_PROVIDER_TEST_ALLOW_RESET=true`;
- `SHARPACCESS_POSTGRES_TEST_CONNECTION_STRING`;
- `SHARPACCESS_POSTGRES_READINESS=true` for readiness evidence;
- native Windows PostgreSQL client tools for recovery evidence.

Unconfigured CI may remain SQLite-only, but the supported-provider release gate requires the approved PostgreSQL scratch database and must fail rather than skip when PostgreSQL evidence is selected.
