# ADR 0012: Publish from a clean release repository

## Status

Accepted on 2026-07-12.

## Context

The existing `JonatanCordoba/dotnet-auth` repository contains the complete private development history, including experimental commits, internal pull requests, and release-preparation evidence. Renaming that repository would preserve its Git history and GitHub collaboration records. Rewriting its history would leave avoidable ambiguity around cached commits, pull-request references, clones, tags, and accidental restoration of old refs.

The first public SharpAccess release should begin with a deliberate, reviewable source snapshot and a clean root commit while preserving the development repository as internal engineering evidence.

## Decision

`JonatanCordoba/dotnet-auth` remains the private development repository through the stable 1.0 release process.

During the final release window, a new empty repository named `JonatanCordoba/SharpAccess` is created first so its canonical URL exists. Repository URLs, Source Link settings, badges, security links, SBOM metadata configuration, and provenance configuration are then updated and committed in `dotnet-auth`. That final development revision is fully validated and approved.

The exact tracked-file tree of the approved revision is exported without the `.git` directory or any Git refs and committed to the empty `JonatanCordoba/SharpAccess` repository as a new signed root commit. Stable tags, releases, packages, SBOMs, checksums, and provenance originate from the clean release repository.

The release repository is not created by renaming, mirroring, forking, or force-rewriting the development repository.

## Consequences

- Public history begins with one curated initial source commit.
- Internal development commits, branches, pull requests, and issues remain in the private development repository.
- The root tree can contain valid canonical repository metadata without release-only edits.
- Release automation must prove that the exported tree and clean root tree match the approved development revision before publication.
- Relevant public issues and documentation may be recreated deliberately, but internal PR and issue history is not imported automatically.
- After release, fixes are developed in the private repository and exported through the same controlled synchronization process unless a later ADR changes the contribution model.

## Guardrails

- The source snapshot must be generated from an immutable approved commit SHA.
- Export uses tracked files only and must exclude `.git`, local artifacts, secrets, caches, test databases, and unpublished internal evidence.
- A deterministic tree manifest and archive checksum must demonstrate equivalence between the approved development revision, the export, and the clean release root commit.
- The release repository must be empty before the initial push and must not be initialized by GitHub with a README, license, or `.gitignore`.
- Package and Source Link metadata may point to `JonatanCordoba/SharpAccess` only after the empty repository exists; that metadata transition must be committed and fully validated in `dotnet-auth` before export.
- No source or metadata file may be edited only in the staging directory or clean release repository. Required changes return to `dotnet-auth`, receive full validation, and are exported again.
- The private development repository must not publish stable NuGet packages directly.
- Deleting or archiving the development repository is a separate retention decision and is not required for a clean public history.
