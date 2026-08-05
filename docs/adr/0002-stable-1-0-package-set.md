# ADR 0002: Coordinate the stable 1.0 package set

## Status

Superseded on 2026-07-22 by ADR 0015.

This ADR is retained as historical decision evidence and is no longer normative.

## Context

SharpAccess is a provider-neutral package family with one core package and four relational provider packages. Publishing only part of that intended stable family would create inconsistent support expectations, package-version drift, and duplicated release policy.

## Decision

The coordinated stable `1.0.0` package set is:

- `SharpAccess.Core`
- `SharpAccess.Sqlite`
- `SharpAccess.Postgres`
- `SharpAccess.SqlServer`
- `SharpAccess.MySql`

All five packages use synchronized versions. Internal or prerelease artifacts may be produced for validation, but stable publication is blocked until every package satisfies its applicable support, compatibility, security, testing, documentation, and release gates.

## Consequences

- Provider implementation may proceed sequentially while stable publication remains coordinated.
- SQLite remains the zero-infrastructure reference path during development.
- Incubation providers must not be described as supported before final promotion.
- Release automation must validate the complete stable package set.

## Guardrails

- `eng/ProviderStatus.props` remains the provider-status source of truth.
- Package metadata and release documentation must use synchronized versions.
- A targeted provider test does not constitute stable-release approval.
