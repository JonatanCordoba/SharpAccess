# Troubleshooting

## Configuration fails at startup

Check for:

- missing or weak signing keys;
- missing token-hashing keys;
- missing password pepper;
- missing dedicated rate-limit partition key;
- reused secret material;
- insecure production `BaseUri`;
- invalid cookie configuration;
- incomplete OIDC provider settings;
- missing selected-provider connection configuration.

Production validation intentionally fails closed.

## Database initialization fails

- Confirm exactly one supported provider is registered.
- Verify the connection string or host-managed data source.
- Verify the database principal has the permissions required for the selected migration mode.
- Check migration serialization and ledger status.
- For PostgreSQL, confirm the approved scratch/production server is reachable and TLS/timeout policy is correct.
- For SQLite, verify directory permissions and that the DB is not on a network filesystem.

Use the migration tool’s `status` and `validate` commands through the PowerShell wrapper.

## Authentication returns 401

Check:

- token algorithm and `kid`;
- issuer, audience, lifetime, and clock skew;
- account active state;
- security version;
- authorization version;
- tenant membership for tenant-scoped tokens;
- signing-key deployment consistency across instances.

## Authorization returns 403

Confirm whether the endpoint requires:

- a global permission;
- an active-tenant permission;
- tenant ownership;
- a specifically documented global-or-tenant policy.

Global and tenant permissions are intentionally not interchangeable.

## Refresh fails

Possible causes include expiration, revocation, replay detection, family revocation, account-state change, key rotation, cookie confirmation failure, or an exceeded family/token bound. Replay is a security event and can revoke the family.

## OIDC callback fails

Verify exact callback URI, issuer, allowed hosts, nonce/state, PKCE, client credentials, system clock, and provider test-user/audience configuration. Never reuse prior authorization codes or PKCE material.

## SQLite is busy

Keep transactions short, monitor contention, confirm WAL/busy-timeout policy, and move to PostgreSQL when the workload needs multiple sustained writers or horizontal scaling.

## PostgreSQL tests are skipped or blocked

The repository intentionally makes ordinary local PostgreSQL execution opt-in, but supported-provider and release evidence requires the protected connection environment. Ensure the expected environment variable is present in the current PowerShell process without printing its value.

## Verification refuses a dirty tree

`verify-local` is clean-tree and revision-bound. Inspect the complete diff, commit the coherent change, then run verification on the exact clean commit.

## References

- [Testing](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/TESTING.md)
- [Operations](Operations)
- [Security](Security)
- [Database Migrations](Database-Migrations)
