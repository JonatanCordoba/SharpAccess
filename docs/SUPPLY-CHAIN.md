# Supply-chain and release evidence

## Evidence boundary

SharpAccess generates one CycloneDX 1.6 document and one SPDX 2.3 document for each active package root:

- `SharpAccess.Core`;
- `SharpAccess.Sqlite`;
- `SharpAccess.Postgres`.

The Windows-only generator reads each committed `packages.lock.json`, resolves the exact checked-out Git revision, hashes either the package archive or project root, emits deterministic dependency documents, and generates the complete set twice. The wrapper requires byte-for-byte equality between both runs.

`sbom-evidence.json` records:

- repository lifecycle identity;
- publication mode;
- Windows platform;
- source revision and commit-derived timestamp;
- package and version;
- dependency count;
- root hash source and SHA-256;
- CycloneDX and SPDX output hashes.

The canonical development and publication identity is `https://github.com/JonatanCordoba/SharpAccess`.

## Package-root modes

Development and ordinary CI require package archives only for projects marked Supported. PostgreSQL may therefore use project-root composition evidence until its coordinated promotion revision.

Stable publication requires:

- Core, SQLite, and PostgreSQL all marked Supported;
- exact package archives for all three;
- the canonical repository identity;
- the clean public root revision.

SQL Server and MySQL are not SBOM roots because they are not active projects.

## Generate evidence

Run on Windows with PowerShell 7:

```powershell
./scripts/sbom.ps1 `
  -RepositoryRoot $PWD `
  -RepositoryUrl https://github.com/JonatanCordoba/SharpAccess `
  -ReplaceExistingOutput
```

For stable publication from the clean root:

```powershell
./scripts/sbom.ps1 `
  -RepositoryRoot $PWD `
  -RepositoryUrl https://github.com/JonatanCordoba/SharpAccess `
  -RequireAllPackageArchives `
  -StablePublication
```

Revision is derived from and must equal checked-out `HEAD`. Caller-supplied timestamps are rejected.

## Toolchain and workflow pins

`global.json` selects the accepted .NET 10 SDK and disables roll-forward. `eng/SupplyChain.props` owns:

- supported Windows/PowerShell tooling;
- development and canonical repository identities;
- active source inputs;
- full-SHA GitHub Action pins.

There are no service-image or container-digest inputs. Workflows do not use Docker or service containers.

## Retained evidence

Windows workflows retain SHA-addressed artifacts for:

- build/test logs;
- Core, SQLite, PostgreSQL, and changed-code coverage;
- provider-contract results;
- mutation and operational evidence;
- SAST SARIF;
- NuGet vulnerability results;
- tracked-secret scan evidence;
- SBOMs and checksums;
- package-consumer validation;
- release export and release-candidate evidence.

`.github/required-checks.json` is the reviewable check-name contract. Apply only `pullRequestRequiredChecks` to PR branch protection. `defaultBranchEvidenceChecks` describes protected PostgreSQL and integrated release evidence that does not run in an untrusted PR context.

## Publication boundary

Stable Core, SQLite, and PostgreSQL packages, archive-root SBOMs, checksums, provenance, tags, and GitHub releases are created only from a verified signed SharpAccess release revision.
