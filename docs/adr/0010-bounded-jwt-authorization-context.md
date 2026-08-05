# ADR 0010: Bound JWT authorization context with reference fallback

## Status

Accepted on 2026-07-12.

## Context

Embedding unbounded roles, permissions, memberships, or tenant context in access tokens creates oversized headers, stale authorization state, disclosure risk, and unpredictable request cost.

## Decision

SharpAccess keeps access-token claims minimal and bounded. When an authorization context exceeds the configured safe claim budget, the token carries an opaque server-side reference instead of the complete authorization set. Immediate persisted account and session invalidation remains the default.

## Consequences

- Small authorization contexts may remain self-contained within explicit limits.
- Large contexts require a bounded server-side lookup.
- Hosts may enable short-lived authorization caching, but it is disabled by default and remains host-controlled.

## Guardrails

- Raw permissions or membership lists must never grow without a configured bound.
- Reference identifiers must be opaque, unguessable, scoped, expiring, and revocable.
- Authorization changes must invalidate or bypass stale referenced context safely.
