# OpenID Connect

SharpAccess implements a generic keyed OpenID Connect profile using Authorization Code + PKCE.

## Security profile

The flow requires:

- exact issuer validation;
- exact supported signing-algorithm validation;
- state and nonce validation;
- PKCE;
- bounded return paths and metadata;
- explicit endpoint host allowlists;
- one-time local exchange after the provider callback;
- HTTPS for non-loopback production origins and callbacks.

Unsupported implicit, password, and other unapproved flows are rejected.

## Provider configuration

Each keyed provider supplies its issuer, client identity, protected client secret, callback, authorization endpoint, token endpoint, JWKS endpoint, and allowed hosts. A configured but disabled provider does not activate its feature requirements.

## Browser flow

1. The browser navigates to the SharpAccess challenge endpoint with a bounded local return path.
2. SharpAccess creates protected state and PKCE material.
3. The provider authenticates the user and returns to the exact callback.
4. SharpAccess validates the response, issuer, signature, nonce, and state.
5. SharpAccess places a short-lived one-time local exchange code in the URL fragment.
6. The browser removes the fragment and exchanges the code through the local SharpAccess endpoint.
7. The host establishes the normal secure refresh session.

The sample’s Google-compatible exchange endpoint is:

```text
POST /auth/oauth/google/exchange
```

## Live evidence

Release evidence requires fresh OIDC authorization material. Authorization codes, PKCE verifiers, nonces, state values, and tokens are one-time or short-lived and must never be reused or retained with secrets.

## References

- [OpenID Connect and OAuth profile](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/OAUTH.md)
- [OIDC live smoke](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/OIDC-LIVE-SMOKE.md)
- [Sample host](https://github.com/JonatanCordoba/SharpAccess/blob/main/samples/SharpAccess.SampleApi/README.md)
- [OIDC architecture decision](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/adr/0014-generic-keyed-openid-connect.md)
