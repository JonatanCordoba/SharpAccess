# Roadmap

## Current release path

The active release candidate is `0.9.0-rc.1`. The canonical package cohort is:

- `SharpAccess.Core`;
- `SharpAccess.Sqlite`;
- `SharpAccess.Postgres`.

The remaining release lifecycle includes exact-revision performance approval, protected RC verification, public hosted verification, Wiki publication, Trusted Publishing, signed tagging, package publication, prerelease creation, and clean consumer validation.

## Future providers

SQL Server and MySQL are roadmap candidates only. They are not active projects, dependencies, package IDs, namespaces, registrations, workflows, scripts, tests, operational runbooks, or release blockers.

A future provider proposal requires a new accepted ADR and evidence plan covering:

- provider-neutral compatibility;
- native Windows DB support;
- migrations and historical upgrades;
- transaction and concurrency behavior;
- error classification;
- restricted-principal operation;
- query plans;
- coverage and mutation evidence;
- backup and recovery;
- package-consumer validation;
- security and operational documentation;
- synchronized release and rollback policy.

No placeholder source or dormant dependency should be introduced before approval.

## Other uncommitted ideas

Redis, passkeys/WebAuthn, additional identity-provider presets, distributed rate limiting implementations, additional databases, vendor-specific managed-key packages, and optional observability exporters remain uncommitted ideas unless separately designed, implemented, tested, documented, and approved.

## Canonical reference

- [Repository roadmap](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/ROADMAP.md)
- [Provider cohort ADR](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/adr/0020-active-provider-cohort.md)
