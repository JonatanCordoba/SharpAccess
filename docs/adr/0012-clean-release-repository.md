# ADR 0012: Publish from a clean release repository

## Status

Accepted on 2026-07-12.

## Context

The existing `JonatanCordoba/dotnet-auth` repository contains the complete private development history, including experimental commits, internal pull requests, and release-preparation evidence. Renaming that repository would preserve its Git history and GitHub collaboration records. Rewriting its history would leave avoidable ambiguity around cached commits, pull-request references, clones, tags, and accidental restoration of old refs.

The first public SharpAccess release should begin with a deliberate, reviewable source snapshot and a clean root commit while preserving the development repository as internal engineering evidence.

## Decision

`JonatanCordoba/SharpAccess` is the canonical and only active development repository after the RC1 history-clean migration.

The initial SharpAccess root was created from the exact privately validated tracked-file tree without inheriting private Git history. Repository URLs, Source Link settings, badges, security links, SBOM metadata, and provenance now belong to SharpAccess.

The exact tracked-file tree of the approved revision is exported without the `.git` directory or any Git refs and committed to the empty `JonatanCordoba/SharpAccess` repository as a new signed root commit. Stable tags, releases, packages, SBOMs, checksums, and provenance originate from the clean release repository.

The release repository is not created by renaming, mirroring, forking, or force-rewriting the development repository.

## Consequences

- Public history begins with one curated initial source commit.
- Historical private development records are preserved outside the active repository as migration provenance.
- The root tree can contain valid canonical repository metadata without release-only edits.
- Release automation must prove that the exported tree and clean root tree match the approved development revision before publication.
- Relevant public issues and documentation may be recreated deliberately, but internal PR and issue history is not imported automatically.
- All fixes and future releases are developed through SharpAccess branches and pull requests.

## Guardrails

- The source snapshot must be generated from an immutable approved commit SHA.
- Export uses tracked files only and must exclude `.git`, local artifacts, secrets, caches, test databases, and unpublished internal evidence.
- A deterministic tree manifest and archive checksum must demonstrate equivalence between the approved development revision, the export, and the clean release root commit.
- The release repository must be empty before the initial push and must not be initialized by GitHub with a README, license, or `.gitignore`.
- Package and Source Link metadata points to `JonatanCordoba/SharpAccess` and is validated from the selected SharpAccess revision.
- No source or metadata file may be edited only in staging. Required changes return to a SharpAccess branch and receive complete validation.
- Stable NuGet packages may be published only by the protected SharpAccess release workflow.
- Deleting or archiving the development repository is a separate retention decision and is not required for a clean public history.
