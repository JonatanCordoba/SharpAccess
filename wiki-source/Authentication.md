# Authentication

SharpAccess supports password authentication, registration, email verification, password reset, account-state validation, fresh-authentication checks, and external OpenID Connect.

## Password security

- Passwords use Argon2id with random salts.
- Peppers are host-owned, versioned, and rotatable.
- Unknown-account paths perform equivalent dummy work.
- Hashing concurrency, queue length, and queue wait are bounded.
- Successful login can rehash credentials under the active pepper.
- Production validation rejects weak or reused secret material.

## Account lifecycle

Registration, verification, reset, password changes, lockout, and status changes use bounded request contracts and sanitized failure behavior. Account state and security version are rechecked for authenticated requests.

## Fresh authentication

Sensitive mutations require a recently issued access token. The host controls the freshness window, which cannot exceed the access-token lifetime. A client receiving a stale-authentication response should sign in again before retrying.

## Email ownership and recovery

Production hosts must register a reliable `IEmailSender`. Verification and reset messages must use the externally visible `BaseUri`. Raw tokens, reset URLs, and verification URLs must not be logged.

## Rate limiting

Mapped authentication features require a dedicated rate-limit partition key. Login, registration, forgot-password, refresh, and enabled OIDC providers are partitioned using bounded, non-secret identifiers and keyed hashes.

## Browser flow

The refresh token is normally carried in a secure HttpOnly cookie. Browser applications should keep the access token in memory, use the request-confirmation header for cookie-backed refresh/logout, and maintain a restrictive content-security policy.

## Related pages

- [Tokens and Refresh](Tokens-and-Refresh)
- [OIDC](OIDC)
- [Security](Security)
- [Security design](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/SECURITY.md)
- [Password breach validation](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/PASSWORD-BREACH-VALIDATION.md)
- [Rate limiting](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/RATE-LIMITING.md)
