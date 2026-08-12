# Contributing to SharpAccess

`JonatanCordoba/SharpAccess` is the canonical source, development, verification, release, and package repository. Use SharpAccess branches and pull requests for implementation, review, documentation, release-control, and package changes.

## Supported contributor environment

- Windows.
- PowerShell 7.
- .NET 10 selected by `global.json`.
- No Bash, Linux/macOS parity, Docker, Compose, or service containers.
- Active provider projects: SQLite and PostgreSQL only.

SQL Server and MySQL remain future roadmap candidates. Do not add placeholder projects, dependencies, namespaces, scripts, tests, or workflows for them without a new accepted ADR and reviewed implementation/evidence plan.

`eng/Version.props` owns the synchronized package version. The currently published prerelease is `0.9.0-rc.1`. Stable `1.0.0` is a future, separately opened release stage.

Keep active paths under the SharpAccess identity. Do not commit patch files, logs, temporary files, orphan lock files, legacy project paths, local credentials, generated artifacts, or phase scratch documents.

## Verification tiers

Use targeted checks while developing. Run complete local verification only after committing a coherent change. Protected PostgreSQL, OIDC, controlled-performance, recovery, package, export, and release-candidate evidence belongs to trusted local or protected workflow execution when a future release revision requires it.

Post-release documentation and repository-governance changes do not require recapture of immutable RC1 PostgreSQL, OIDC, performance, package, or publication evidence.

## Change procedure

Run targeted checks before staging. Review the complete staged diff and commit it. Then run the exact clean revision:

```powershell
./scripts/verify-structure.ps1 -RepositoryRoot $PWD
dotnet test tests/SharpAccess.PackageTests/SharpAccess.PackageTests.csproj --configuration Release --no-restore
./scripts/verify-local.ps1 -RepositoryRoot $PWD -Version '0.9.0-rc.1'
git status --short
```

The final status command must produce no output. When lock files need intentional refresh, run `scripts/refresh-lock-files.ps1`, inspect every dependency change, commit the result, and rerun the complete gate.

Provider tests may reset only an approved `sharpaccess_contract_tests*` database and require explicit reset acknowledgment. Never place database credentials or protected OIDC values in source, logs, or retained evidence.

After integration, evidence that must bind to the integrated revision is rerun against the resulting protected `main` commit. Branch-head and integrated-commit verification are different revision identities. For ordinary post-release maintenance, use the normal protected PR checks and the exact committed-tree verification required by the current change policy.
