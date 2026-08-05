# ADR 0009: Support host-managed connections and data sources

## Status

Accepted on 2026-07-12.

## Context

Enterprise hosts often own connection pools, credential rotation, cloud identity integration, health checks, and database lifetime management. Requiring every provider to construct its own connection from a string would duplicate resources and prevent host governance.

## Decision

Each provider must support a provider-appropriate host-managed connection or data-source abstraction in addition to documented connection-string configuration. SharpAccess borrows connections for an operation but does not dispose a host-owned pool or data source.

## Consequences

- Hosts can integrate SharpAccess with existing database resource governance.
- Provider contracts must define ownership, async disposal, cancellation, transaction, and concurrency behavior.
- The core package remains independent of concrete provider connection types.

## Guardrails

- Provider-specific connection types remain in provider packages.
- A borrowed connection must not escape its operation scope.
- Connection factories must propagate `CancellationToken` and preserve host ownership.
