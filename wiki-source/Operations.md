# Operations

The host owns production deployment, service objectives, monitoring, database capacity, backups, secret storage, email delivery, proxy configuration, and incident response. SharpAccess supplies bounded package behavior and repeatable engineering evidence.

## Production preparation

Before serving users:

- configure HTTPS and trusted proxies;
- protect independent signing, hashing, pepper, rate-limit, OIDC, Data Protection, database, and email secrets;
- persist and protect Data Protection keys;
- use a least-privilege DB principal;
- initialize or validate the schema through a controlled migration process;
- register a production email sender;
- configure monitoring and alerting;
- complete backup and restore exercises;
- size the deployment with representative data and topology.

## Observability

Monitor:

- authentication successes and failures;
- 401, 403, and 429 responses;
- lockouts;
- token replay and family revocation;
- reset and verification failures;
- role, permission, membership, ownership, and account-status changes;
- OIDC state, nonce, callback, and exchange failures;
- migration and DB availability failures.

Audit evidence is not a substitute for operational telemetry. Do not add secrets or raw tokens to custom diagnostic dimensions.

## Change control

Key rotation, provider configuration, migrations, capacity limits, rate limits, and release changes require an explicit reviewed operational plan.

## Provider notes

- SQLite: monitor disk, lock contention, WAL, and file permissions.
- PostgreSQL: monitor pool saturation, timeouts, query plans, replication/backup policy, and native recovery procedures.

## References

- [Operations reference](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/OPERATIONS.md)
- [Deployment](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/DEPLOYMENT.md)
- [Observability](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/OBSERVABILITY.md)
- [Production hardening](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/production-hardening.md)
- [Incident response](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/INCIDENT-RESPONSE.md)
