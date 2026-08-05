# ADR 0016: License SharpAccess under MIT

## Status

Accepted on 2026-07-22. Supersedes ADR 0005.

## Context

SharpAccess is intended to be a broadly usable ASP.NET Core authentication and authorization package family. The previous permanent AGPL-3.0-only decision imposed adoption and redistribution constraints that no longer match the approved product strategy.

## Decision

SharpAccess source, packages, documentation, SBOM metadata, and release artifacts use the MIT License.

## Consequences

- `LICENSE` contains the standard MIT License text.
- Package metadata uses the SPDX expression `MIT`.
- Dependency-license review, notices, SBOM generation, and provenance remain required.
- Historical ADR 0005 remains in the repository as a superseded decision record.

## Guardrails

- Active package metadata, documentation, release controls, and the repository license file must agree on MIT.
- A future license change requires another explicit governance decision.
- Vendored schemas and dependency metadata may retain third-party SPDX identifiers and license text required by their upstream formats.
