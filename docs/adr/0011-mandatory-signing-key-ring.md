# ADR 0011: Require a rotatable JWT signing key ring

## Status

Accepted on 2026-07-12.

## Context

A single static JWT signing secret cannot support overlap during rotation, targeted revocation, durable key identity, or asymmetric verification without coordinated downtime.

## Decision

Stable SharpAccess JWT signing uses a host-provided key ring with explicit key identifiers (`kid`). The ring supports an active signing key and overlapping verification keys. Asymmetric signing is supported and is the preferred production model.

## Consequences

- Token validation selects keys by `kid` and rejects unknown or disallowed keys.
- Rotation can occur without invalidating every unexpired token immediately.
- Hosts own secure key storage, distribution, activation, retirement, and emergency revocation.

## Guardrails

- Production must not fall back to an embedded or generated signing secret.
- Key identifiers must be unique and stable for a key's lifetime.
- Algorithm selection must be allowlisted and must not be controlled by untrusted token input.
- Rotation, overlap, retirement, and rollback behavior require automated tests and operational documentation.
