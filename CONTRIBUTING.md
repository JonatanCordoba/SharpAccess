# Contributing to SharpAccess

`JonatanCordoba/dotnet-auth` is the private development system of record. Use it for implementation, review, and prerelease evidence. Public release-candidate and stable packages, canonical tags, and public GitHub releases are created only from the exact verified clean `JonatanCordoba/SharpAccess` repository.

## Supported contributor environment

- Windows.
- PowerShell 7.
- .NET 10 selected by `global.json`.
- No Bash, Docker, Compose, or service containers.
- Active provider projects: SQLite and PostgreSQL only.

SQL Server and MySQL remain future roadmap candidates. Do not add placeholder projects, dependencies, namespaces, scripts, tests, or workflows for them without a new accepted ADR and reviewed implementation plan.

`eng/Version.props` owns the synchronized package version. The current release-candidate version is `0.9.0-rc.1`. Stable versioning requires the canonical repository identity and the release-only gate; repository URL metadata alone is not identity evidence.

Keep active paths under the SharpAccess identity. Do not commit patch files, logs, temporary files, orphan lock files, legacy project paths, local credentials, generated artifacts, or phase scratch documents.

## Verification tiers

Use targeted checks while developing. Use complete local verification only after committing a coherent change. Protected PostgreSQL, OIDC, controlled-performance, recovery, package, export, and release-candidate evidence belongs to trusted local or protected workflow execution.

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

After a squash merge, rerun complete verification against the resulting `master` commit before using it as release evidence. Branch-head verification and squash-merge verification are evidence for different commit SHAs.
