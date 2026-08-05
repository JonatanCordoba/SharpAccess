# ADR 0007: Separate global and tenant authorization catalogs

## Status

Accepted on 2026-07-12.

## Context

Global administration and tenant-scoped authorization have different trust boundaries. Deriving global authority from tenant membership or reusing one ambiguous role catalog can permit cross-tenant privilege escalation and makes negative authorization behavior difficult to audit.

## Decision

SharpAccess maintains separate global and tenant authorization catalogs.

- Global roles and permissions authorize product-wide operations.
- Tenant roles and permissions authorize operations only within the selected tenant.
- Tenant ownership uses a separate immutable tenant `Owner` role.
- Cross-tenant administration requires an explicit global permission and never derives from tenant permissions.

## Consequences

- Persistence contracts and migrations must represent the two scopes explicitly.
- JWT and server-side authorization contexts must preserve scope without ambiguous claim reuse.
- Existing authorization data requires an explicit migration classification.

## Guardrails

- Tenant identifiers supplied by a client are not trusted without membership and policy validation.
- Global permission checks must not query or aggregate tenant role assignments.
- Negative tests must cover cross-tenant access, owner invariants, scope confusion, and privilege escalation.
