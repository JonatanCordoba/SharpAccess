# Contributing

`JonatanCordoba/SharpAccess` is the canonical source and development repository.

## Environment

- Windows.
- PowerShell 7.
- .NET 10 selected by `global.json`.
- No Bash, Docker, Compose, or service containers.
- Active provider projects: SQLite and PostgreSQL only.

## Change workflow

1. Create a narrow SharpAccess branch.
2. Implement one coherent scope.
3. Run targeted validation.
4. Inspect status, diff check, diff stat, and the complete diff.
5. Commit the tracked change.
6. Run complete `verify-local` on the exact clean commit.
7. Repair and amend the unpushed commit if necessary.
8. Rerun complete verification.
9. Push only after the exact commit passes.
10. Open a SharpAccess pull request.
11. Merge with a strategy compatible with revision/ancestry evidence.
12. Fast-forward local `main` and verify the exact integrated revision.

`verify-local` requires a clean tracked tree.

## Common validation

```powershell
./scripts/verify-structure.ps1 -RepositoryRoot $PWD
dotnet test tests/SharpAccess.PackageTests/SharpAccess.PackageTests.csproj `
    --configuration Release `
    --no-restore
./scripts/verify-local.ps1 -RepositoryRoot $PWD -Version '0.9.0-rc.1'
git status --short
```

The final status command must produce no output.

## Security and evidence

Never commit credentials, DB connection strings, protected OIDC values, generated evidence, logs, temporary files, or machine-specific paths. Provider tests may reset only an explicitly approved scratch database.

## References

- [Contributing guide](https://github.com/JonatanCordoba/SharpAccess/blob/main/CONTRIBUTING.md)
- [Testing](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/TESTING.md)
- [Quality gates](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/QUALITY-GATES.md)
- [Repository governance](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/repository-governance.md)
