# ADR 0008: Expose explicit migration modes with a safe production default

## Status

Accepted on 2026-07-12.

## Context

Automatic schema mutation during application startup can exceed runtime database permissions, create multi-instance races, and make deployment rollback difficult. Hosts still need deterministic validation, script generation, and controlled local initialization.

## Decision

SharpAccess migration behavior is explicit and supports these modes:

- `ValidateOnly`
- `Apply`
- `GenerateScript`
- `Disabled`

Production defaults to `ValidateOnly`. Applying migrations is an explicit host or deployment action. Local development may opt into `Apply` through documented configuration.

## Consequences

- Runtime and migration database principals may be separated.
- Startup fails safely when required schema is missing or incompatible under `ValidateOnly`.
- Providers must generate deterministic, ordered migration evidence and preserve historical upgrade paths.

## Guardrails

- Migration application must be idempotent, transactional where supported, and concurrency-safe.
- Validation must not silently mutate schema.
- Destructive migration behavior requires explicit documentation, compatibility review, and rollback planning.
