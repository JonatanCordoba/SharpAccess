# SharpAccess repository bootstrap and legacy decommission record

## Purpose

The SharpAccess repository bootstrap is complete. This document records the completed history-clean migration from `JonatanCordoba/dotnet-auth` and defines the preservation boundary for eventual destructive retirement of the legacy repository.

Do not execute the historical bootstrap again against the existing SharpAccess repository.

## Repository roles

| Repository | Current role |
|---|---|
| `JonatanCordoba/SharpAccess` | Canonical source, development, review, verification, Wiki-source, release, and package-publication repository |
| `JonatanCordoba/dotnet-auth` | Historical private migration source retained only until preservation/decommission gates pass |

The canonical SharpAccess source branch is `main`. The `master` branch is used only by the separate GitHub Wiki repository.

## Completed history-clean migration

The signed SharpAccess root commit is:

- commit: `d792a4ec42e63c831aa135de6dcbaf3fe65a665f`
- commit message: `Initial SharpAccess 0.9.0-rc.1 source migration`
- tracked tree: `2d17de6b48614afd1aaad678a3c8f646b1f361ec`

The final tracked legacy migration tree used for parity evidence is also `2d17de6b48614afd1aaad678a3c8f646b1f361ec`. That tree equality is the tracked-file migration-parity proof: the selected legacy tracked content was present in the new history-clean SharpAccess root before SharpAccess subsequently advanced through its own protected history.

The migration intentionally did not import legacy commit history, branches, tags, notes, replace refs, or pull-request refs into SharpAccess history.

## Relationship to RC1

The migration root is not the RC1 package provenance commit. The immutable published RC1 package provenance commit is `4595545d8afd84c58795fc02c2c242533cdff1ac` and the signed release tag is `v0.9.0-rc.1`.

Later post-release `main` commits do not change either historical identity. RC1 publication details are owned by `docs/RELEASE-EVIDENCE-MATRIX.md`.

## Legacy preservation boundary

Permanent deletion of `JonatanCordoba/dotnet-auth` is a separate destructive action. Before deletion, preserve or explicitly classify every legacy asset that must survive, including:

- complete Git history through a verified Git bundle plus SHA-256;
- final legacy revision/tree identity;
- repository settings and relevant security/governance state;
- issues and pull-request history requiring retention or historical/obsolete classification;
- releases/tags, workflow runs/artifacts, publication and package evidence;
- consumer-validation evidence;
- Wiki and Discussions state, including an explicit record when those features were absent;
- other repository metadata required to explain migration and retirement.

Tracked-tree parity alone does not prove external GitHub state has been preserved.

## Destructive retirement gate

Passing the preservation/parity audit does not authorize deletion. Permanent deletion requires a separate explicit authorization naming `JonatanCordoba/dotnet-auth` and an immediate pre-delete revalidation of repository identity, expected final revision/tree, verified bundle/hash, preservation inventory, and SharpAccess canonical status.

Any identity drift, missing preservation evidence, or unexplained legacy-only asset blocks deletion.

## Stable boundary

Legacy retirement does not start or authorize stable `1.0.0` work. Stable remains a future, separately opened stage.
