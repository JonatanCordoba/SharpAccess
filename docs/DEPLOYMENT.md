# Deployment

SharpAccess supported deployment is Windows-only.

## Required production configuration

Configure independent secrets through a protected secret manager/environment rather than committed files: JWT signing, opaque-token HMAC, rate-limit HMAC, password peppers, OIDC client credentials, cookie confirmation, Data Protection protection, database credentials, and SMTP/transactional-email credentials.

## Data Protection

Production hosts must persist Data Protection keys outside ephemeral storage and protect them at rest. Example Windows paths:

```text
APP_DATA_PROTECTION_KEYS_DIRECTORY=C:\ProgramData\SharpAccess\DataProtection-Keys
APP_DATA_PROTECTION_CERTIFICATE_PATH=C:\ProgramData\SharpAccess\Secrets\sharpaccess-data-protection.pfx
APP_DATA_PROTECTION_CERTIFICATE_PASSWORD=...
APP_DATA_PROTECTION_APP_NAME=SharpAccess.SampleApi
```

Use the same application name, protected key directory, and certificate across instances that must read the same protected state/cookies. Back up keys with the application recovery plan.

## Reverse proxy, HTTPS, cookies, and browser clients

Apply trusted forwarded headers before HTTPS/HSTS, CORS, `UseSharpAccess`, and endpoints. Set `BaseUri` to the externally visible HTTPS origin. Keep OIDC endpoint hosts on explicit allowlists.

Outside local development, use secure refresh cookies and required browser request confirmation for cookie-backed refresh/logout. Do not return refresh tokens in response bodies for browser applications. Protect the host from XSS with an appropriate CSP and avoid browser persistent storage for access tokens.

## Database, email, and observability

Run SharpAccess schema initialization/validation before traffic. Use SQLite only within its single-writer/local-filesystem constraints; use PostgreSQL for server/multi-instance needs and follow `POSTGRES-OPERATIONS.md`.

Register a production email sender with retry/observability/bounce handling and never log verification/reset URLs or raw tokens.

Monitor authentication/authorization/rate-limit/session/OIDC/database/migration/audit signals without adding secrets or personal identifiers to telemetry.
