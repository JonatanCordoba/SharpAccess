# Privacy and data handling

SharpAccess processes authentication, authorization, tenant, OAuth identity, session, and security-audit data. The consuming organization determines its legal role, lawful basis, notices, retention, data-subject procedures, and jurisdiction-specific obligations.

This document is engineering guidance and does not claim GDPR, Argentine Law 25.326, ISO/IEC 27701, or other legal compliance.

## Data categories

Typical persisted data includes:

- email address and normalized lookup value;
- password hash metadata, salt, and pepper version identifier;
- roles, permissions, tenant memberships, and security version;
- opaque-token hashes and session metadata;
- OAuth provider subject and verified email association;
- IP address and User-Agent fields in refresh-token and audit records;
- security event timestamps and bounded details.

Raw passwords, raw refresh tokens, password-reset tokens, email-verification tokens, OAuth codes, peppers, and signing keys must not be persisted in ordinary records or telemetry.

## Responsibility matrix

| Area | Package | Host or operator |
|---|---|---|
| Data minimization | Uses bounded auth records and telemetry tags | Enables only required features and claims |
| Retention | Exposes persistence operations | Defines and executes retention/deletion policy |
| Access control | Enforces authorization contracts | Restricts database, logs, backups, and support access |
| Encryption | Uses cryptographic protections for credentials and tokens | Provides transport encryption, secret storage, backup encryption |
| Data-subject requests | No automated legal workflow | Verifies identity and coordinates export, correction, or deletion |
| Breach response | Security controls and documentation | Notification assessment, regulator/user communication |
| Cross-border processing | Not selected by package | Selects regions, subprocessors, and contractual safeguards |

## Logging and telemetry

Do not enrich SharpAccess activities or metrics with email, user, tenant, IP, User-Agent, token, or request-body values. Centralized logs must apply redaction and access controls. Audit records and diagnostic logs require separate retention policies.

## Retention

Define explicit periods for:

- active and revoked refresh-token metadata;
- expired reset and verification records;
- OAuth state and exchange records;
- security audit records;
- application logs and traces;
- backups and operational evidence.

Retention must account for security investigation, legal obligations, minimization, and deletion propagation to backups where required.

## Review triggers

Review privacy impact when adding claims, audit fields, telemetry dimensions, external providers, new regions, new processors, exports, or administrative data-access features.
