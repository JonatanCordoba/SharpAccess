# Deployment

## Required production configuration

Configure secrets through a secret manager or protected environment variables, never committed configuration files:

- JWT signing key;
- opaque-token HMAC key;
- dedicated rate-limit partition HMAC key;
- current password pepper and any historical pepper still needed for login;
- each enabled OpenID Connect provider client secret;
- cookie request-confirmation value;
- Data Protection key directory and certificate material;
- database connection string;
- SMTP or transactional-email credentials.

Use independent random values for each purpose. `RateLimits.PartitionKey` is mandatory whenever a mapped rate-limited authentication feature is enabled, must contain at least 32 bytes, and cannot reuse signing, token-hashing, pepper, or provider-client material. Rotate JWT and token-hashing keys under an explicit incident or migration plan; rotating either immediately invalidates affected tokens.

## Data Protection

Production hosts must persist Data Protection keys outside ephemeral storage and protect them at rest. The sample host requires these settings in Production:

```text
APP_DATA_PROTECTION_KEYS_DIRECTORY=/var/lib/sharpaccess/keys
APP_DATA_PROTECTION_CERTIFICATE_PATH=/run/secrets/sharpaccess-data-protection.pfx
APP_DATA_PROTECTION_CERTIFICATE_PASSWORD=...
APP_DATA_PROTECTION_APP_NAME=SharpAccess.SampleApi
```

Use the same application name, key directory, and certificate across instances that must read the same OAuth state payloads and cookies. Back up the key directory with the application database. Losing the keys invalidates active protected payloads.

## Reverse proxy order

Apply trusted forwarded headers first, then HTTPS redirection/HSTS, CORS as needed, `UseSharpAccess`, and endpoints. Set `BaseUri` to the externally visible HTTPS origin. Non-loopback production origins and every OpenID Connect callback must use HTTPS. Keep each provider's authorization, token, and JWKS hosts in its explicit `AllowedHosts` list and do not permit HTTP redirects to expand that trust boundary.

For the sample host, set `APP_FORWARDED_HEADERS_KNOWN_PROXIES` to a comma- or semicolon-delimited list of trusted reverse-proxy IP addresses. Keep `APP_FORWARDED_HEADERS_LIMIT=1` unless more than one trusted proxy hop is intentionally deployed.

## Cookies and browser clients

Keep `RefreshTokenCookieSecurePolicy=Always` outside local development. Non-loopback production hosts must use a `__Secure-` refresh-cookie name and must require the request-confirmation header for cookie-backed refresh and logout. Defaults:

```text
APP_REQUIRE_COOKIE_CONFIRMATION_HEADER=true
APP_COOKIE_CONFIRMATION_HEADER_NAME=X-SharpAccess-CSRF
APP_COOKIE_CONFIRMATION_HEADER_VALUE=1
```

The package default refresh cookie is `sharpaccess_refresh`. In production, configure a `__Secure-` prefixed name such as `__Secure-sharpaccess_refresh`. Pre-v1 cookie and confirmation-header names are not accepted.

Do not enable `ReturnRefreshTokenInResponseBody` for browser applications. Protect the host from XSS with a restrictive content-security policy and avoid storing access tokens in local or session storage.

## Fresh authentication

Sensitive mutations require a recently issued access token. Keep `APP_FRESH_AUTHENTICATION_MINUTES` short enough for administration workflows; the default is 10 minutes and it cannot exceed the access-token lifetime. Users who receive a stale-token response should sign in again before retrying the sensitive action.

## Database

Run `InitializeSharpAccessAsync` before serving requests. Back up SQLite consistently and monitor disk space, lock contention, and migration failures. For high write concurrency or multiple application instances, use a server database provider such as PostgreSQL instead of sharing SQLite over a network filesystem.

## Email

Register a production `IEmailSender` with retry, provider authentication, observability, and bounce handling. Do not log verification/reset URLs or raw tokens.

## Observability

Monitor 401, 403, 429, lockout, token-reuse, password-reset, role-change, permission-change, user-status, tenant membership, and OAuth failure events. Audit records intentionally bound IP/User-Agent/detail lengths; avoid adding secrets or raw tokens to custom details.
