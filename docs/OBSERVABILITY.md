# Observability

SharpAccess emits provider-neutral diagnostic signals through .NET `System.Diagnostics`. It does not require an OpenTelemetry package or select an exporter.

## Signal identity and privacy

The package activity source and meter are `SharpAccess`. Signals use bounded operation/outcome/error-type dimensions and intentionally exclude emails, user/tenant IDs, IP/User-Agent values, passwords, raw tokens, result codes, SQL, connection strings, and exception messages. Hosts must preserve this redaction boundary when enriching/exporting telemetry.

Recommended views include authentication failure ratios, refresh-reuse events, duration percentiles, unexpected exceptions, lockout/rate-limit trends, database/migration failures, and OIDC callback validation failures. These are host-specific operational signals, not package SLAs.

## Audit versus telemetry

Telemetry is best-effort operational evidence and does not replace mandatory security-audit records. Security-sensitive provider mutations commit their canonical audit row inside the owning transaction; failure rolls the mutation back. Explicit standalone observations such as login/recovery/OIDC outcomes follow the bounded best-effort policy documented by ADR 0013 and `docs/SECURITY.md`.

## Validation

Diagnostics tests verify activity/meter behavior, observation-failure semantics, cancellation, and sensitive-data exclusion. Operational-readiness validation is Windows-only under the current SharpAccess platform contract.
