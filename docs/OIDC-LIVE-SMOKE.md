# Protected OpenID Connect live smoke

The default clean-clone verification path is deterministic and makes no external identity-provider calls. Real-provider evidence is integrated into the manually dispatched, environment-protected Windows release-candidate workflow.

## What the smoke proves

The smoke sends one just-in-time authorization code and matching PKCE verifier to the configured provider token endpoint, retrieves live JWKS, and applies the same issuer, audience, lifetime, algorithm, nonce, authorized-party, subject, verified-email, response-size, redirect, and host-allowlist rules used by `SharpAccess.Core`.

It does not persist provider access or refresh tokens and does not create screenshots. Retained evidence contains only the commit, completion time, configuration, pass/fail status, and a fixed redaction declaration.

## Protection requirements

Create a GitHub environment named `release-candidate` and require reviewer approval. Restrict who may deploy to it. The workflow has only `workflow_dispatch` and must not expose protected settings to untrusted pull-request code.

Configure these environment variables:

```text
SHARPACCESS_OIDC_LIVE_PROVIDER
SHARPACCESS_OIDC_LIVE_CLIENT_AUTHENTICATION_METHOD
SHARPACCESS_OIDC_LIVE_BASE_URI
SHARPACCESS_OIDC_LIVE_CALLBACK_PATH
SHARPACCESS_OIDC_LIVE_AUTHORIZATION_ENDPOINT
SHARPACCESS_OIDC_LIVE_TOKEN_ENDPOINT
SHARPACCESS_OIDC_LIVE_JWKS_ENDPOINT
SHARPACCESS_OIDC_LIVE_VALID_ISSUERS
SHARPACCESS_OIDC_LIVE_SIGNING_ALGORITHMS
SHARPACCESS_OIDC_LIVE_ALLOWED_HOSTS
```

Use semicolons for list values. `ClientAuthenticationMethod` must be `ClientSecretPost` or `ClientSecretBasic`.

Configure these environment secrets:

```text
SHARPACCESS_OIDC_LIVE_CLIENT_ID
SHARPACCESS_OIDC_LIVE_CLIENT_SECRET
SHARPACCESS_OIDC_LIVE_AUTHORIZATION_CODE
SHARPACCESS_OIDC_LIVE_CODE_VERIFIER
SHARPACCESS_OIDC_LIVE_NONCE
```

The authorization code, verifier, and nonce must be generated for the exact client, redirect URI, and authorization request immediately before approval and dispatch. They are single-use and short-lived. Replace or delete those three secrets after the run. Never place them in workflow inputs, command-line arguments, issues, pull-request comments, artifacts, or chat transcripts.

## Running the integrated workflow

1. Prepare a dedicated provider test account with no production data or privileges.
2. Generate one authorization-code request using PKCE S256 and the exact callback URI.
3. Complete consent interactively without recording screenshots.
4. Store the returned code, verifier, and nonce as protected environment secrets.
5. Dispatch `integrated release candidate` and approve the `release-candidate` environment deployment.
6. Confirm the retained OIDC artifact contains only `oidc-live-smoke.json` and no TRX, token, code, nonce, endpoint, email, subject, account data, or response body.
7. Rotate or delete the ephemeral code, verifier, and nonce secrets.

## Trusted local operator entry point

After setting the same environment contract in a trusted Windows PowerShell 7 session:

```powershell
./scripts/oidc-live-smoke.ps1 -RepositoryRoot $PWD
```

Normal test runs skip the live fact when the environment contract is absent. The script fails before testing when a required value is missing, preventing a skipped fact from being mistaken for evidence.

## Failure handling

A failure artifact still contains no provider response body or secret value. Treat the authorization code as spent after any token-endpoint attempt. Investigate with provider-side audit records and bounded local diagnostics; do not enable HTTP body logging or upload raw test results.
