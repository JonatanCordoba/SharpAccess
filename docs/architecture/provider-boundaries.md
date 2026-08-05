# Provider boundary rules

Provider packages are responsible for durable storage. The core package is responsible for provider-neutral authentication behavior.

## Core package may own

- Public ASP.NET Core registration and middleware extensions.
- Options and options validation.
- Endpoint mapping and endpoint handlers.
- Password hashing and password-risk extension points.
- Token creation and validation.
- Feature switches.
- Provider contracts.
- ProblemDetails and security middleware integration.

## Provider packages must own

- Database dependencies.
- Connection factories.
- SQL dialects.
- Command creation.
- Transactions.
- Ordered migrations.
- Schema initialization.
- Provider-specific exception handling.
- Provider-contract test compliance.

## Forbidden in the core package

- Concrete database references such as SQLite-specific packages.
- Provider-specific SQL.
- Provider-specific migration scripts.
- ORM assumptions.
- Host infrastructure such as Docker or Python tooling.

## Provider implementation expectations

- Use parameterized commands.
- Keep multi-row security-sensitive operations transactional.
- Keep migrations idempotent.
- Enable database constraints such as foreign keys when supported.
- Return provider-neutral domain records and service results.
- Do not leak provider-specific exceptions into public responses.

## Review questions

Before adding or changing a provider abstraction, ask:

1. Is this a real boundary a second provider would implement differently?
2. Is the abstraction exercised by current provider code?
3. Is there a provider-contract test that verifies the expected behavior?
4. Would deleting the abstraction simplify the implementation without reducing portability?

If the answer to the first three questions is no, prefer removing or collapsing the abstraction.
