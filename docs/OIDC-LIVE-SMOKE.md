# Protected OpenID Connect live smoke

Normal clean-clone verification is deterministic and performs no real identity-provider call. Real-provider evidence is a protected Windows release-evidence stage.

## What it proves

The live smoke exchanges one just-in-time authorization code and matching PKCE verifier, retrieves live JWKS, and applies the same issuer, audience, lifetime, algorithm, nonce, authorized-party, subject, verified-email, response-size, redirect, and host-allowlist rules used by SharpAccess.

It does not persist provider access/refresh tokens or screenshots. Retained evidence is fixed, bounded, and redacted.

## Protected configuration

The integrated `.github/workflows/release-candidate.yml` workflow owns the protected RC/release orchestration. Protected OIDC settings/secrets must be stored only in the approved GitHub environment and never exposed to untrusted pull-request code or workflow inputs.

The authorization code, verifier, and nonce are single-use/short-lived and must be generated for the exact client/redirect request immediately before approval/dispatch, then rotated/deleted after use. Never place them in issues, pull-request comments, command-line arguments, artifacts, or chat transcripts.

## Trusted local entry point

With the same protected environment contract in a trusted Windows PowerShell 7 session:

```powershell
./scripts/oidc-live-smoke.ps1 -RepositoryRoot $PWD
```

The protected evidence path must fail when required values are absent so a skipped live test cannot be mistaken for success.

## RC1 state

RC1 protected OIDC evidence is completed historical release evidence. Post-release documentation/control synchronization does not require a new authorization code or a new live-provider run merely because `main` advanced.

A future selected release reruns live OIDC evidence only when required by its current policy.
