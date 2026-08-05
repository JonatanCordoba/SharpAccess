# ADR 0018: Use focused internal use cases

## Status

Accepted.

## Context

Authentication changes are high risk because regressions can affect lockout, token rotation, email-token consumption, tenant isolation, and audit behavior. Large application services make those flows harder to review and harder to test in isolation.

The package must preserve a small public API for NuGet consumers while keeping internal logic easy to reason about.

## Decision

Endpoint-facing authentication behavior is routed through focused internal use cases for registration, password login, refresh sessions, current-user context, password change, password reset, and email verification.

Shared session issuance lives in a dedicated internal session issuer so access-token creation, authorization-context construction, and refresh-token record creation are not duplicated across flows.

`IAuthService` remains the internal endpoint-facing facade so endpoint handlers do not need to know about every use case.

## Consequences

Positive:

- Security reviews can focus on one flow at a time.
- Endpoint handlers remain thin.
- Password-risk validation stays as a wrapper concern around selected use cases.
- Future tests can target individual flows without exercising unrelated behavior.

Trade-offs:

- There are more internal classes.
- Shared cross-flow behavior needs helpers to avoid duplication.
- The facade must stay intentionally thin and should not regain business logic.

## Guardrails

- Keep use cases internal.
- Keep endpoint handlers free of business logic.
- Keep password-risk checks outside the core flow implementation.
- Keep session issuance centralized.
- Add adversarial tests before changing token, tenant, password, or verification behavior.
