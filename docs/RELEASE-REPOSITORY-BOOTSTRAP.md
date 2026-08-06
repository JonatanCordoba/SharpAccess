# Clean SharpAccess release repository bootstrap

## Repository roles

| Repository | Role | History policy |
|---|---|---|
| `JonatanCordoba/dotnet-auth` | Historical private migration source | Retains the original engineering history until preservation and deletion gates pass. |
| `JonatanCordoba/SharpAccess` | Canonical public source and package-publication repository | Begins with one curated signed root commit. |

The initial SharpAccess root was created as a deterministic tracked-file snapshot of the privately validated source tree, without inherited Git history.

## Release identity

The first public release candidate is:

- package version: `0.9.0-rc.1`;
- signed tag: `v0.9.0-rc.1`;
- initial public commit message: `Initial SharpAccess 0.9.0-rc.1 source release`;
- GitHub release classification: prerelease.

Stable `1.0.0` is a later, separately gated release.

## Preconditions

Before export:

1. Core, SQLite, and PostgreSQL remain Supported.
2. SQL Server and MySQL remain roadmap-only and absent from the active tree.
3. Package, security, migration, operational, performance, recovery, SBOM, checksum, provenance, OIDC, and consumer-smoke evidence is complete for the selected development revision.
4. No unresolved Critical, High, or release-blocking Moderate finding remains.
5. The complete protected release-candidate matrix passes for the exact approved development SHA.
6. The generated README quality snapshot is committed in SharpAccess, verified again, and stable on regeneration.
7. The deterministic export dry run passes for that same final SHA.
8. `JonatanCordoba/SharpAccess` exists without generated starter files or imported private history. If an existing target is not empty, stop and obtain an explicit, reviewed disposition; do not overwrite it implicitly.
9. Canonical repository, package, security, badge, Wiki, and release metadata is committed and fully revalidated in SharpAccess.
10. The final candidate is identified by an immutable full SHA and tree SHA.
11. The bounded public-repository bootstrap sequence has explicit operator authorization.

Creating an empty target is not publication. It establishes the canonical identity required by package metadata and final verification.

## Private quality-snapshot fixed point

The README quality table is generated from `artifacts/quality-report/metrics.json`. Because committing the generated table changes the selected revision, complete this fixed-point cycle in SharpAccess before release:

1. Run exact-revision verification on the candidate:

   ```powershell
   ./scripts/verify-local.ps1 `
     -RepositoryRoot $PWD `
     -Version '0.9.0-rc.1'
   ```

2. Generate the README snapshot and, when preparing the Wiki locally, the matching Wiki quality page:

   ```powershell
   ./scripts/Update-PublicQualitySnapshot.ps1 `
     -RepositoryRoot $PWD `
     -WikiQualityPath '<local-wiki-path>\Quality-and-Metrics.md'
   ```

3. Review the generated p95 and worst-observed values.
4. Commit the README change through the normal private pull-request path.
5. Rerun exact-revision verification on the resulting commit.
6. Run `Update-PublicQualitySnapshot.ps1` again.
7. Require `git diff --exit-code -- README.md` to pass.
8. Run the complete protected release-candidate matrix on that same final revision.

If regeneration changes the README, repeat the reviewed commit and verification cycle. Do not export until the private revision is a stable fixed point.

The Wiki is a separate public repository surface. Its generated quality page must use the same final metrics and is verified after Wiki publication; it does not change the canonical source-tree export.

## Complete private release gate

Run the exact protected matrix before export:

```powershell
./scripts/release-candidate.ps1 `
  -RepositoryRoot $PWD `
  -Version '0.9.0-rc.1' `
  -ReferenceEnvironment 'controlled-windows-runner-01' `
  -RequirePostgres `
  -RequireOidcLiveEvidence `
  -RequireApprovedPerformanceBaseline
```

A required stage must fail rather than silently skip. Missing, expired, infrastructure-blocked, or not-run protected evidence is not passing evidence.

## Export dry run

```powershell
$releaseSha = (git rev-parse HEAD).Trim()
./scripts/export-dry-run.ps1 `
  -RepositoryRoot $PWD `
  -Revision $releaseSha
```

The dry run:

- uses `git archive` from the immutable revision;
- excludes inherited Git objects and refs;
- rejects tracked secrets, caches, local databases, build output, and evidence directories;
- compares exported blob identities with the source tree;
- creates a temporary clean Git index;
- requires the clean-root tree SHA to equal the source tree SHA;
- retains archive checksum and normalized manifests under `artifacts/release-export`.

## Windows snapshot procedure

Run from PowerShell 7 on Windows after the final SharpAccess revision is approved. Use a clean release directory outside the normal working clone.

```powershell
$releaseSha = '<approved-final-development-sha>'
$workspace = '<approved-release-workspace>'
$development = Join-Path $workspace 'sharpaccess-development'
$release = Join-Path $workspace 'SharpAccess'
$archive = Join-Path $env:TEMP "sharpaccess-$releaseSha.tar"

if (Test-Path $development) {
    throw "Development staging path already exists: $development"
}
if (Test-Path $release) {
    throw "Release staging path already exists: $release"
}

git clone --no-tags git@github.com:JonatanCordoba/SharpAccess.git $development
if ($LASTEXITCODE -ne 0) { throw 'Development clone failed.' }

git -C $development checkout --detach $releaseSha
if ($LASTEXITCODE -ne 0) { throw 'Detached checkout failed.' }

New-Item -ItemType Directory -Path $release | Out-Null

git -C $development archive --format=tar --output=$archive $releaseSha
if ($LASTEXITCODE -ne 0) { throw 'Tracked-file archive failed.' }

tar -xf $archive -C $release
if ($LASTEXITCODE -ne 0) { throw 'Archive extraction failed.' }
Remove-Item -LiteralPath $archive -Force

git -C $release init -b main
if ($LASTEXITCODE -ne 0) { throw 'Public-root initialization failed.' }

git -C $release add -A
if ($LASTEXITCODE -ne 0) { throw 'Public-root staging failed.' }

git -C $release commit -S -m 'Initial SharpAccess 0.9.0-rc.1 source release'
if ($LASTEXITCODE -ne 0) { throw 'Signed public root commit failed.' }
```

The release root must contain tracked files only and no inherited private history.

## Required equivalence evidence

Before push, verify and record:

```powershell
$developmentTree = (git -C $development rev-parse "$releaseSha^{tree}").Trim()
$publicRoot = (git -C $release rev-parse HEAD).Trim()
$publicTree = (git -C $release rev-parse 'HEAD^{tree}').Trim()
$commitCount = [int](git -C $release rev-list --count HEAD)

if ($commitCount -ne 1) {
    throw "Expected one public commit; found $commitCount."
}
if ($developmentTree -cne $publicTree) {
    throw "Tree mismatch: development=$developmentTree public=$publicTree"
}
```

Record:

- approved development commit SHA;
- approved development tree SHA;
- deterministic tracked-file manifest;
- archive SHA-256;
- normalized source/export manifest comparison;
- final clean-root tree SHA;
- final public root commit SHA and signature result;
- one-commit history count;
- operator and reviewer identities;
- completed change and release records.

No source or metadata difference is allowed. Required changes must return to a SharpAccess branch, pass the complete gate, and be selected again.

## Empty target rules

Create or prepare `JonatanCordoba/SharpAccess` with:

- no generated README;
- no generated license;
- no generated `.gitignore`;
- no imported issues or pull requests;
- no mirrored refs, tags, branches, notes, replace refs, or pull-request refs.

Never force-push over an unexplained existing public history.

## Push the final root

Only after the root is final, signed, verified, equivalent, and explicitly authorized:

```powershell
git -C $release remote add origin git@github.com:JonatanCordoba/SharpAccess.git
git -C $release push -u origin main
```

After push, record the remote public root SHA and verify its tree and normalized manifest against the reviewed local root.

## After the root push

1. Configure `main` protection/rulesets and required Windows checks.
2. Configure CODEOWNERS, private vulnerability reporting, Dependabot, secret scanning, push protection, and protected release environments.
3. Confirm repository URLs, Source Link, all three NuGet badges, security links, SBOM identity, and provenance point to `JonatanCordoba/SharpAccess`.
4. Run complete Windows release verification from the exact pushed root.
5. Run `Update-PublicQualitySnapshot.ps1` against the public root and require it to produce no README diff. This confirms the exact public-root metrics reproduce the committed table without changing the source tree.
6. Generate final Core, SQLite, and PostgreSQL runtime packages, symbol packages, SBOMs, checksums, and provenance from that exact root.
7. Obtain separate explicit authorization for the RC tag and public package publication.
8. Create and push the signed `v0.9.0-rc.1` tag.
9. Publish in dependency order from the verified tagged root only: Core, then SQLite and PostgreSQL.
10. Create the GitHub release as a prerelease and attach the reviewed release assets.
11. Publish the reviewed Wiki and verify its generated quality table against the same public-root metrics.
12. Run post-publication clean-consumer smoke against nuget.org.

## Development repository after release

`dotnet-auth` is the historical private migration source. It may be deleted only after its Git bundle, discussions, settings, release evidence, publication evidence, Wiki, packages, and consumer-validation results are preserved. After deletion, SharpAccess is the only workable repository.
