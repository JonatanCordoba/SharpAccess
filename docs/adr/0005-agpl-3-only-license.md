# ADR 0005: Use AGPL-3.0-only permanently

## Status

Superseded on 2026-07-22 by ADR 0016.

This ADR is retained as historical decision evidence and is no longer normative.

## Context

The package family needs one unambiguous license across source, packages, documentation, and release artifacts. License drift or permissive compatibility language would create legal and distribution uncertainty.

## Decision

SharpAccess source and stable packages use `AGPL-3.0-only`. The project does not offer an alternative license through repository metadata, package metadata, or release documentation.

## Consequences

- Hosts and distributors must evaluate AGPL-3.0-only obligations before adoption.
- Package metadata, notices, SBOMs, and documentation must agree.
- A future license change would require an explicit legal and governance decision and cannot be inferred from a documentation edit.

## Guardrails

- `LICENSE` and `PackageLicenseExpression` must remain consistent.
- Dependency license review remains part of release evidence.
- Documentation must not describe SharpAccess as MIT, dual-licensed, or permissively licensed.
