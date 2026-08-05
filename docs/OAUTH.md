# OpenID Connect and OAuth 2.1 profile

SharpAccess exposes a provider-neutral, keyed OpenID Connect contract under
`AuthOptions.OpenIdConnect.Providers`. The provider key is the stable lowercase
identifier used by routes, persisted state, external-account links, cache
partitions, and audit details. The default options retain one disabled
Google-compatible `google` entry; it is an example configuration rather than a
Google-specific public API.

The implementation targets `draft-ietf-oauth-v2-1-15`, the active OAuth 2.1
Internet-Draft available when this repository was prepared. OAuth 2.1 is not
represented here as a final RFC.

## Provider configuration

Each enabled provider declares its client ID and secret, `ClientSecretPost` or
`ClientSecretBasic` token-endpoint authentication, exact callback path, HTTPS
authorization/token/JWKS endpoints, exact valid issuers, requested scopes,
accepted signing algorithms, and an explicit endpoint host allowlist.
Provider keys are bounded lowercase identifiers. Enabled callback paths are
unique literal paths without route syntax, whitespace, percent escapes, or dot
segments, and they cannot shadow a SharpAccess route under the mapped endpoint
prefix. Scopes must include `openid` and `email`, and accepted algorithms are
restricted to RS256, PS256, or ES256. Automatic HTTP redirects are disabled,
and responses that leave the configured allowlist are rejected.

The built-in Google-compatible entry preserves Google's current endpoints, the
accepted `https://accounts.google.com` and legacy `accounts.google.com`
issuers, `openid email profile` scopes, RS256, and the required Google hosts.
Hosts supply only their own client credentials and explicitly enable the
entry. The Google-compatible entry explicitly uses `ClientSecretPost`.

## Challenge

`GET /auth/oauth/{provider}/challenge` generates cryptographically random
`state`, PKCE verifier, and nonce values. Only a keyed hash of `state` is
searchable in the database. The verifier and nonce are protected with ASP.NET
Core Data Protection, and the complete record expires after a short configured
interval.

The authorization request fixes:

- `response_type=code`;
- `code_challenge_method=S256`;
- the provider's configured scopes;
- an exact redirect URI;
- state and nonce.

Return URLs are bounded local paths. Unknown or disabled provider keys fail
without altering persistence keys or redirect targets.

## Callback and identity validation

The exact configured callback path atomically consumes state before exchanging
the code. The token request sends the original PKCE verifier and exact redirect
URI and authenticates by exactly one configured client-secret method. The ID
token must have a valid signature, an explicitly accepted algorithm and issuer,
the configured client ID as audience, a valid lifetime, exactly one bounded
Unix-seconds issued-at claim no later than the project clock plus skew, a
matching nonce, a matching authorized party whenever present and whenever
multiple audiences are declared, a stable subject, and a verified email.

A provider subject already linked to a user resolves that user. A matching
local email is linked only when the local email is already verified and the
account is active. Otherwise the flow fails safely. A new OpenID Connect-only
user is created verified and active with the standard User role.

Creating a new external-account binding commits one `oauth_account_linked`
audit row in the same provider transaction as the binding and any new local
user and baseline role. Resolving an existing binding does not duplicate that
canonical evidence; `oauth_login_success` remains the separate best-effort
outcome observation.

Provider access and refresh tokens are not stored. The callback creates a
short-lived, one-time local exchange code and places it in the return URL
fragment. The frontend exchanges that code through
`POST /auth/oauth/{provider}/exchange` for a local session.

## Unsupported flows

The implicit grant, resource-owner password grant, token-bearing query
parameters, wildcard redirect URIs, dynamic discovery from untrusted issuers,
unbounded provider redirects, symmetric ID-token algorithms, and
unverified-email linking are not supported.

## Deterministic validation

`OidcEmulatorIntegrationTests` exercises the complete generic flow with a keyed
`emulator` provider, SQLite persistence, the real `OpenIdConnectOAuthProvider`,
a signed RSA identity token, and a JWKS response served by an in-process HTTP
handler. It verifies authorization code flow, PKCE S256, nonce binding, token
endpoint request shape, signature and claims validation, safe account creation,
local access and refresh session issuance, atomic state consumption, and
one-time local exchange-code consumption without network access or reusable
credentials.

The emulator deliberately does not configure or reference the default `google`
entry. Passing this test is evidence that orchestration, persistence, cache
partitioning, and session issuance are keyed-provider behavior rather than a
Google architectural special case.

## Protected live-provider evidence

The real-provider check is isolated in `.github/workflows/oidc-live-smoke.yml`.
It is manual-only, bound to the protected `oidc-live-smoke` environment, and
requires a just-in-time authorization code, matching PKCE verifier, and nonce.
Normal builds skip the live fact. The paired scripts fail when the protected
environment contract is incomplete and retain only a fixed redacted JSON
record under `artifacts/operations/oidc-live-smoke`.

Credentials, authorization codes, PKCE verifiers, nonces, identity tokens,
provider response bodies, subjects, emails, screenshots, and account data must
not be printed or uploaded. See [OIDC-LIVE-SMOKE.md](OIDC-LIVE-SMOKE.md) for the
operator and environment-protection contract.

## Google compatibility decision for stable 1.0

Google remains the first disabled configuration entry in the generic provider
dictionary. Stable 1.0 keeps that source-compatible options shape and its
current endpoint defaults. There is no public `GoogleOptions`, Google-specific
service registration method, Google-specific orchestration service, or
provider-specific callback handler to deprecate or remove. Hosts that already
enable the `google` dictionary entry continue to use the same provider key and
callback path unless they explicitly reconfigure them.

Future Google endpoint, issuer, authentication-method, or scope changes are
configuration maintenance inside the generic contract. Any future removal of
the built-in example entry requires a separately documented compatibility plan
and cannot silently replace keyed provider configuration before the stable API
freeze.
