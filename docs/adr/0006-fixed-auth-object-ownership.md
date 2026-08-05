# ADR 0006: Own fixed `auth_*` database objects for v1

## Status

Accepted on 2026-07-12.

## Context

SharpAccess must coexist with host application data while retaining deterministic schema, migration, indexing, and security behavior across providers. Arbitrary table renaming would expand the compatibility surface and make migrations and support evidence unreliable.

## Decision

For stable v1, each provider owns a fixed set of SharpAccess relational objects using the `auth_*` naming convention. Hosts may choose the physical database or supported schema/catalog placement, but they may not remap individual SharpAccess table or column names.

## Consequences

- SharpAccess can run in a shared physical database without integrating with a host ORM model.
- Provider migrations and support procedures operate on a predictable object set.
- Existing host objects that collide with reserved `auth_*` names must be resolved before installation.

## Guardrails

- Provider-owned SQL remains outside `SharpAccess.Core`.
- Migration validation must identify ownership and collision problems before applying changes.
- Destructive reset tooling remains restricted to explicitly approved test databases.
