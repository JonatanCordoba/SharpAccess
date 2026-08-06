# PostgreSQL Provider

`SharpAccess.Postgres` is the supported server DB provider. Its support claim depends on continuing native Windows evidence for provider contracts, migrations, concurrency, restricted principals, query plans, recovery, coverage, and package consumers.

## Install

```powershell
dotnet add package SharpAccess.Core --version 0.9.0-rc.1
dotnet add package SharpAccess.Postgres --version 0.9.0-rc.1
```

## Register

```csharp
builder.Services.AddSharpAccess(builder.Configuration);
builder.Services.AddPostgresAccess(builder.Configuration);
```

The host supplies the connection string or a host-managed data source. Do not commit connection strings.

## Operational contract

- The host owns physical pooling and connection configuration.
- Timeouts and cancellation are bounded.
- TLS and certificate validation belong to the host’s approved connection policy.
- Migration serialization prevents concurrent schema mutation.
- Production principals should be least-privilege.
- Provider transactions preserve canonical audit evidence and rollback behavior.
- Native query-plan evidence must retain intended indexes for bounded keyset pagination.

## Evidence interpretation

`SharpAccess.Postgres` is classified as Supported by the repository policy. That classification does not convert a skipped, cancelled, timed-out, or infrastructure-blocked hosted job into passing release evidence. The applicable release revision requires a successful `postgres-native` run and retained provider-contract, coverage, and recovery artifacts.

## Required local and hosted environment

PostgreSQL tests use an approved scratch database and the protected connection setting expected by the repository scripts. Hosted evidence is scoped through the `postgres-evidence` environment. The connection value must not appear in source, logs, Wiki content, chat summaries, or retained evidence.

## Validation

```powershell
./scripts/provider-contracts.ps1 -RepositoryRoot $PWD -RequirePostgres
./scripts/postgres-quality-coverage.ps1 -RepositoryRoot $PWD
./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

Use the repository’s canonical commands and exact parameters for the selected release phase.

## References

- [PostgreSQL operations](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/POSTGRES-OPERATIONS.md)
- [Provider package README](https://github.com/JonatanCordoba/SharpAccess/blob/main/providers/SharpAccess.Postgres/README.md)
- [Provider contract testing](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/PROVIDER-CONTRACT-TESTING.md)
- [Provider status](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/PROVIDER-STATUS.md)
