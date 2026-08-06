# Package consumer validation

Public-surface tests protect exported types, but they do not prove that a real consumer can install and compile against the generated NuGet packages. The package consumer smoke scripts validate the downstream NuGet experience from a temporary ASP.NET Core application.

## Goal

Verify that the packed `SharpAccess.Core` and `SharpAccess.Sqlite` packages can be installed and used by a clean ASP.NET Core host without project references.

## Implemented smoke scripts

Run after `scripts/pack` has produced package artifacts:

```powershell
./scripts/package-smoke.ps1 -RepositoryRoot $PWD
```

The scripts:

1. Require `artifacts/packages` to contain both supported runtime NuGet packages.
2. Create a temporary ASP.NET Core `net10.0` app under the system temp directory.
3. Configure a local NuGet source that points at the freshly packed artifacts.
4. Install `SharpAccess.Core` and `SharpAccess.Sqlite` as package references.
5. Fail if the smoke app uses project references.
6. Compile the intended consumer integration shape:
   - `AddSharpAccess(builder.Configuration, options => ...)`
   - `AddSqliteAccess(builder.Configuration, options => ...)`
   - `UseSharpAccess()`
   - `MapSharpAccessEndpoints()`
7. Build with warnings as errors.

## Why this is different from public API tests

Reflection-based public API tests prove that exported type names and members did not drift. A consumer smoke test proves the package can actually be restored and compiled the way downstream applications will use it.

The smoke test can catch:

- Missing files in the `.nupkg`.
- Incorrect package metadata.
- Broken transitive dependency assumptions.
- Extension-method namespace regressions.
- Provider package install or native SQLite asset issues.
- Accidental reliance on project references.

## Future runtime extension

A later hardening pass should extend this smoke test to start the temporary app, initialize the auth schema, and hit `/health` plus at least one auth endpoint. The current gate intentionally starts with package restore/compile validation because that is the highest-signal check for NuGet asset and public integration regressions.

## Release gate recommendation

For production release candidates, run this validation on the approved Windows environment after pack and before publishing artifacts.