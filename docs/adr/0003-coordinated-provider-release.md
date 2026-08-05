# ADR 0003: Release relational providers together

## Status

Superseded on 2026-07-22 by ADR 0015.

This ADR is retained as historical decision evidence and is no longer normative.

## Context

The provider projects share a public product identity and must offer equivalent security, migration, persistence, package, and operational guarantees. Independent stable promotion would make the meaning of `1.0.0` vary by provider and increase compatibility risk.

## Decision

SQLite, PostgreSQL, SQL Server, and MySQL are promoted to the stable `1.0.0` support set in one coordinated release. Providers may remain at different implementation stages before that release, but public support status changes only through the final coordinated promotion decision.

## Consequences

- Provider work is ordered SQLite, PostgreSQL, SQL Server, then MySQL.
- A provider can accumulate internal evidence without being publicly supported.
- Stable publication waits for the slowest provider's release-blocking gates.
- Documentation must distinguish current implementation status from the coordinated stable-release target.

## Guardrails

- Provider status is changed only in `eng/ProviderStatus.props` with matching tests and documentation.
- No incubation provider may expose supported public registration or stable package output.
- Provider promotion requires real-engine, migration, package-consumer, coverage, security, and operational evidence.
