# ADR 0001: Keep the core provider-neutral

## Status

Accepted.

## Context

SharpAccess provides authentication and authorization primitives to ASP.NET Core hosts through a provider-neutral Core package. The currently supported persistence provider packages are SQLite and PostgreSQL.

A production package should not force all consumers to accept a database dependency they do not use, and it should not make the core authentication engine depend on provider-specific SQL, migrations, connection types, or package references.

## Decision

The core package remains provider-neutral.

- The core package owns public endpoint registration, middleware integration, options, validation, token services, password services, feature switches, and provider contracts.
- Concrete providers own database dependencies, SQL dialects, migration scripts, connection factories, command construction, and provider-specific errors.
- The SQLite provider package references the core package.
- The core package must not reference SQLite, Dapper, EF Core, ASP.NET Core Identity, or provider-specific SQL.
- Provider implementations must satisfy provider-contract tests rather than changing core behavior for provider quirks.

## Consequences

Positive:

- Consumers install only the provider packages they need.
- Future providers can be added without bloating the core package.
- Provider behavior can be validated through shared contracts.
- Security-sensitive service logic can remain independent from database implementation details.

Trade-offs:

- Provider contracts require careful maintenance.
- Some provider abstractions may look verbose for a single-provider implementation.
- Integration tests must cover both the core behavior and provider behavior.

## Guardrails

- Public core APIs must remain small and stable.
- Database interfaces should remain internal until there is a strong compatibility reason to expose them.
- Provider contracts should exist only at meaningful boundaries.
- Any new concrete provider dependency in the core package is an architecture violation.
