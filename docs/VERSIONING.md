# Versioning

`eng/Version.props` owns the synchronized package version for Core, SQLite, and PostgreSQL.

## Current public version

The first public prerelease, `0.9.0-rc.1`, is published. Its immutable package provenance commit is `4595545d8afd84c58795fc02c2c242533cdff1ac` and its signed tag is `v0.9.0-rc.1`.

Current `main` may advance after publication without changing that historical package provenance.

## Pre-1.0 policy

Until stable `1.0.0` is explicitly opened and released, SharpAccess may still make reviewed breaking changes within the pre-1.0 contract. Any public API/schema/package change must be documented, covered by public API/migration/package-consumer tests, and reflected in the selected release evidence.

## Stable boundary

Stable `1.0.0` is a future, separately gated stage. It requires a newly selected exact revision and the then-current stable evidence matrix. RC1 passage does not imply stable readiness.

After stable release, semantic-versioning/compatibility policy applies to the released stable line and database migration compatibility must be preserved according to the published contract.

SQL Server and MySQL remain deferred and do not receive compatibility promises until separately accepted and published.
