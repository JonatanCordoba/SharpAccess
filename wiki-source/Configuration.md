# Configuration

SharpAccess configuration is host-owned. Production secrets must come from a protected secret manager or equivalent OS-level injection, not tracked JSON.

## Core identity and token settings

Configure:

- `BaseUri` as the externally visible origin.
- `JwtIssuer` and `JwtAudience`.
- `AccessTokenSigning.ActiveKeyId` plus the active and retained verification keys.
- `TokenHashing.CurrentKeyVersion` plus accepted token-hashing keys.
- `Passwords.CurrentPepperVersion` plus accepted pepper versions.
- `RateLimits.PartitionKey` whenever a mapped rate-limited feature is enabled.
- `Features` for password authentication, registration, reset, refresh, administration, tenancy, and enabled OIDC providers.

Use independent random values for every secret role. Production validation rejects weak, predictable, undersized, or reused material.

## Database connection

The host supplies the selected provider connection through supported provider configuration or a host-managed connection factory.

A common configuration shape is:

```json
{
  "ConnectionStrings": {
    "Auth": "<host-owned DB connection string>"
  }
}
```

Register exactly one provider:

```csharp
builder.Services.AddSqliteAccess(builder.Configuration);
// or
builder.Services.AddPostgresAccess(builder.Configuration);
```

## Cookies and browser clients

Outside local development:

- keep the refresh cookie secure;
- use a `__Secure-` prefixed cookie name for non-loopback production hosts;
- require the request-confirmation header for cookie-backed refresh and logout;
- do not return refresh tokens in response bodies for browser applications;
- keep access tokens out of local storage and session storage.

The documented request-confirmation defaults are:

```text
APP_REQUIRE_COOKIE_CONFIRMATION_HEADER=true
APP_COOKIE_CONFIRMATION_HEADER_NAME=X-SharpAccess-CSRF
APP_COOKIE_CONFIRMATION_HEADER_VALUE=1
```

## Data Protection

Persist Data Protection keys outside ephemeral storage and protect them at rest. Instances that must read the same OAuth state payloads, cookies, or opaque cursors must share the same application name and protected key ring.

## OpenID Connect

Each enabled provider requires:

- exact issuer and callback configuration;
- authorization, token, and JWKS endpoints within explicit host allowlists;
- client ID and protected client secret;
- Authorization Code + PKCE;
- nonce and state validation.

See [OIDC](OIDC).

## Production checklist

- HTTPS and trusted proxy configuration.
- Protected independent secrets.
- Durable Data Protection keys.
- Least-privilege database principal.
- Production email sender.
- Backups and tested restore.
- Observability for authentication, authorization, rate limits, replay, and account-state events.

## Canonical references

- [Deployment](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/DEPLOYMENT.md)
- [Production hardening](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/production-hardening.md)
- [Cryptography](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/CRYPTOGRAPHY.md)
- [Persistence and connections](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/PERSISTENCE-AND-CONNECTIONS.md)
- [Rate limiting](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/RATE-LIMITING.md)
