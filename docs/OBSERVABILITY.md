# Observability

SharpAccess emits provider-neutral diagnostic signals through the .NET `System.Diagnostics` APIs. It does not require an OpenTelemetry package and does not select an exporter.

## Signal identity

- Activity source: `SharpAccess`
- Meter: `SharpAccess`
- Instrumentation version: `1.0.0`

The package emits activities named `sharpaccess.auth.<operation>` for the core authentication service.

## Metrics

| Instrument | Type | Meaning |
|---|---|---|
| `sharpaccess.auth.operations` | Counter | Completed, failed, cancelled, or faulted authentication operations |
| `sharpaccess.auth.failures` | Counter | Failed or faulted authentication operations |
| `sharpaccess.auth.duration` | Histogram, milliseconds | End-to-end duration of the authentication service operation |
| `sharpaccess.audit.observation_failures` | Counter | Failed best-effort writes for standalone persisted audit observations |

## Tags

The bounded tag set is:

- `sharpaccess.operation`
- `sharpaccess.outcome`
- `sharpaccess.error.type`

Allowed outcomes are `success`, `failure`, `cancelled`, and `exception`.

`sharpaccess.audit.observation_failures` has no tags. It is intentionally bounded and cannot disclose the event, account, tenant, provider, request, or storage failure.

Signals intentionally exclude email addresses, user IDs, tenant IDs, IP addresses, User-Agent values, passwords, raw tokens, result codes, SQL, connection strings, and exception messages. Hosts must keep this redaction boundary when enriching telemetry.

## Host configuration

A host can subscribe by source and meter name using its selected .NET diagnostics or OpenTelemetry integration. Exporters, collectors, backends, sampling, and dashboards remain host-owned dependencies and must not be added to `SharpAccess.Core`.

## Recommended views and alerts

Define host-specific thresholds rather than treating these examples as a package SLA:

- login failure ratio by deployment and time window;
- refresh-token reuse audit events;
- authentication duration percentiles;
- unexpected exception rate;
- lockout and rate-limit trends;
- database connectivity and migration failures;
- OAuth callback validation failures.

Avoid alerting directly on individual account identifiers. Use aggregate, bounded dimensions.

## Audit evidence is not telemetry

Activities and metrics are best-effort operational signals. They do not satisfy the mandatory security-audit contract and their exporter availability never controls a database transaction. Password, account-status, external-account-binding, session, authorization, and tenant mutations instead commit one canonical row to `auth_security_audit_logs` inside the provider transaction. An audit insert failure rolls back the mutation and is surfaced as an operation exception; a successful retry creates a fresh evidence identifier.

The explicit standalone-observation boundary covers `login_success`, `login_failed`, `password_reset_requested`, `email_verification_requested`, `oauth_login_success`, and `oauth_login_failed`. Those records describe an attempt or outcome but are not the canonical evidence that authorizes a provider mutation. A non-cancellation storage failure increments `sharpaccess.audit.observation_failures` and cannot turn an already-determined response into a failure. Caller cancellation still propagates. This boundary never substitutes for transaction-local mandatory evidence. See [ADR 0013](adr/0013-atomic-security-audit-evidence.md).

## Validation

`DiagnosticsTests` verifies activity and meter output, standalone-observation failure semantics, cancellation propagation, and the absence of sensitive values or sensitive tag names. The operational-readiness scripts run those tests on Linux and Windows.
