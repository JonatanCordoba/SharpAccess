# Production hardening guide

SharpAccess provides secure defaults, but the Windows host and deployment environment own production controls. Treat this as deployment guidance before exposing authentication endpoints to real users.

## Transport, proxy, and browser controls

Serve public endpoints over HTTPS. Configure only trusted reverse proxies before authentication middleware. Set the externally visible HTTPS `BaseUri`, exact OIDC redirect URIs, narrow CORS origins, secure refresh cookies, and the required cookie request-confirmation behavior for browser refresh/logout flows.

## Secrets and keys

Store JWT signing material, token-hashing keys, rate-limit partition keys, password peppers, SMTP/OIDC credentials, cookie-confirmation secrets, Data Protection keys/certificates, and database credentials outside source and deployment artifacts. Use independent material for each role and documented rotation/revocation procedures.

SharpAccess supports rotatable JWT signing-key rings and versioned token-hashing/password-pepper overlap. Retire historical verification material only after the relevant bounded lifetime/migration window or through an explicit incident response.

## Database and recovery

Use least-privilege credentials, provider-appropriate backups, tested restoration, and controlled migrations. SQLite files/WAL/SHM require protected local filesystem permissions. PostgreSQL hosts require reviewed TLS, principals, backup/recovery, query-plan/capacity, and operational policy.

## Email, OIDC, rate limits, logging, audit, and CORS

Use a production email sender; exact OIDC allowlists/redirects; host-appropriate distributed throttling where process-local limits are insufficient; secret-free logs/telemetry; explicit audit retention; and restrictive browser policy. Never log raw tokens, passwords, OAuth codes, peppers, signing keys, or authentication request bodies.

## Operational readiness checklist

Before production launch:

- Windows build/tests/coverage/package/SBOM/API/provider checks required by the selected release/deployment policy pass.
- Dependency/security controls have no unresolved release-blocking finding.
- Secrets are injected through protected Windows/host secret storage.
- Public origin and OIDC callbacks are HTTPS and correct.
- Email sender and database backup/restore are verified.
- Data Protection keys are durable and protected.
- Audit/log retention and redaction are configured.
- Incident response covers session revocation, key/pepper compromise, provider recovery, and package rollback.

No Linux/macOS/Docker parity is part of the supported SharpAccess deployment contract.
