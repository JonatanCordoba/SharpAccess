# ADR 0013: Commit mandatory security audit evidence atomically

## Status

Accepted on 2026-07-17.

## Context

Security-sensitive mutations previously committed provider state before a service appended its audit row. A database failure, process termination, or cancellation in that gap could leave a password, account status, session, authorization, or tenant change without its required evidence. Retrying a failed append could also create duplicate canonical rows.

The four relational providers already own the transaction boundaries for these mutations. Audit evidence contains request metadata known by Core and entity identifiers that are sometimes known only after provider reads or inserts.

## Decision

Every mandatory security mutation and its one canonical audit row commit in the same provider transaction. Core creates a bounded, sanitized evidence record before starting the mutation and passes it through a responsibility-specific persistence contract. The provider enriches only identifiers derived from trusted persisted state, inserts the mutation and audit row on the same connection and transaction, and commits them together.

An audit insert failure is a mutation failure: the provider rolls the transaction back and propagates the exception. A caller may retry with a newly generated audit identifier. Providers do not silently downgrade mandatory evidence to best effort and services do not append a duplicate post-commit canonical row.

Refresh rotation carries a complete bundle of pre-created outcome records. The provider selects exactly one record for each outcome that changes state: success, replay, invalid user, expiration, or configured-family-limit enforcement. A token already observed as revoked enters a dedicated provider replay transaction before user, tenant, or authorization-context preflight. A not-found or unchanged outcome writes no canonical row. Explicit administrator reseeding likewise commits the account, role, session invalidation, and `administrator_seeded` evidence together. A newly created external-account binding commits `oauth_account_linked` evidence with the binding and any newly created local user and baseline role; resolving an existing binding does not duplicate that canonical row.

Standalone persisted observations use a separate `TryWriteObservationAsync` boundary. The closed event set is `login_success`, `login_failed`, `password_reset_requested`, `email_verification_requested`, `oauth_login_success`, and `oauth_login_failed`. These records describe an attempt or outcome but are not canonical authorization for a provider mutation. They therefore cannot change a response whose result was already determined: a non-cancellation storage failure is swallowed after incrementing the tag-free `sharpaccess.audit.observation_failures` counter. A caller token that is already cancelled prevents the write, and caller cancellation during the write still propagates. This boundary has no automatic retry and never replaces transaction-local mandatory evidence.

## Consequences

- Password changes and resets, email verification, user activation or revocation, administrator reseeding, external-account binding, refresh rotation, replay handling and revocation, global role and permission changes, tenant creation and ownership transfer, tenant membership, and tenant-role changes cannot commit without their canonical evidence.
- Duplicate audit identifiers deliberately fail closed and roll back the associated mutation.
- Provider store interfaces require evidence-bearing overloads; legacy convenience overloads construct bounded fallback evidence and delegate in one direction.
- Request IP address, User-Agent, event type, and detail are bounded before reaching provider SQL. Passwords, raw tokens, authorization codes, secrets, and exception details remain prohibited.
- Provider implementations share the same transaction-local insert shape but retain provider-native connections, transactions, SQL, and error behavior.
- Failed standalone observations are visible through one bounded counter while committed request semantics remain stable.
- No schema migration or outbox is required because the existing audit table participates directly in the mutation transaction.

## Guardrails

- Tests must exercise rollback on an audit uniqueness failure and successful retry with a fresh identifier.
- Structural tests must cover all four providers, the transaction-local audit helper, canonical event taxonomy, and absence of post-commit double writes.
- Code after a successful refresh rotation must not perform work that can strand the replacement token from the client; response construction occurs before the commit point.
- Only the enumerated standalone event set may use `TryWriteObservationAsync`; adding an event requires revisiting this decision and its tests.
- Best-effort telemetry is operational observation and cannot satisfy this persistence requirement.

## Rollback

Reverting this decision requires reverting the evidence-bearing persistence contracts, provider transaction changes, and canonical event ownership together. Removing only one layer would reintroduce unaudited commits or duplicate rows. Existing audit rows and schema remain compatible.
