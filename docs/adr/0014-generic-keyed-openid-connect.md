# ADR 0014: Use a generic keyed OpenID Connect contract

## Status

Accepted on 2026-07-17.

## Context

The pre-1.0 public options exposed `AuthFeatureOptions.GoogleOAuth`,
`AuthOptions.GoogleOAuth`, and `GoogleOAuthOptions`, even though OAuth
orchestration and persistence were already provider-neutral. That surface
made one vendor part of the package contract, duplicated feature enablement,
and made a second standards-compatible provider require another public type,
route set, handler set, HTTP client, and validation branch.

The replacement must preserve Authorization Code, PKCE S256, state, nonce,
issuer, audience, signature, algorithm, exact redirect, JWKS, endpoint-host,
replay, bounded-return-URL, and safe account-linking controls. Existing sample
deployments also need a straightforward Google configuration path.

## Decision

The supported public contract is `AuthOptions.OpenIdConnect.Providers`, keyed
by a bounded lowercase provider identifier. Each value is an
`OpenIdConnectProviderOptions` object containing explicit enablement, client
credentials, bounded `ClientSecretPost` or `ClientSecretBasic` token-endpoint
authentication, callback path, authorization/token/JWKS endpoints, valid
issuers, scopes, signing algorithms, allowed hosts, and optional prompt.

The default dictionary contains one disabled `google` entry populated with
Google-compatible endpoints, issuers, scopes, RS256, callback path, prompt,
and allowed hosts. The sample continues to accept `OAUTH_GOOGLE_*` environment
variables, but translates them directly into `Providers["google"]`; this is
an operational bridge, not a retained Google-specific package API.

Challenge and exchange routes use `/auth/oauth/{provider}/...`. Each enabled
provider maps its exact configured callback path with provider metadata. One
generic adapter performs token exchange and ID-token validation for every
configured provider. Automatic HTTP redirects are disabled, and token/JWKS
responses must remain inside the provider's explicit host allowlist.

Because the package has not reached stable 1.0, the Google-specific public
types and feature flag are removed instead of kept as obsolete aliases.
Configuration fails on unsafe provider names, duplicate callback paths,
missing OpenID/email scopes, untrusted issuers or hosts, weak/unknown
algorithms, or incomplete enabled entries.

## Consequences

- Consumers configure one consistent provider dictionary and route shape.
- Google remains easy to enable without defining a vendor-specific contract.
- Adding a standards-compatible provider does not expand the public type
  surface or duplicate orchestration.
- Pre-1.0 source using the removed Google properties must move values into
  `OpenIdConnect.Providers["google"]`.
- Persisted provider keys remain `google` for existing Google links and OAuth
  state, so no schema or data migration is needed.
- Provider access and refresh tokens remain unpersisted; local exchange codes
  remain bounded, short-lived, and single-use.

## Guardrails

- Provider keys, callback paths, issuers, scopes, algorithms, and hosts are
  validated at startup.
- Authorization, token, and JWKS endpoints must be absolute HTTPS URIs without
  credentials, query strings, or fragments.
- Provider scopes include `openid` and `email`; symmetric and `none` ID-token
  algorithms are rejected.
- State is consumed once, PKCE and nonce are mandatory, audiences and
  authorized-party claims bind to the configured client, and return URLs stay
  local.
- Callback paths are unique and provider responses cannot enlarge the
  configured network trust boundary through redirects.
- Public API and endpoint tests prevent Google-only types, flags, or handlers
  from returning.
