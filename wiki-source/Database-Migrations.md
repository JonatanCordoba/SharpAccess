# Database Migrations

SharpAccess uses an immutable ordered migration catalog shared through provider-neutral APIs and implemented by each active DB provider.

## Modes

The repository supports explicit migration, validation, status, and script-generation workflows. Production hosts should not rely on uncontrolled schema mutation during startup.

## Initialization

A host may initialize through:

```csharp
await app.Services.InitializeSharpAccessAsync(
    app.Lifetime.ApplicationStopping);
```

Use the repository documentation to select the appropriate migration mode for local development, deployment, or validation.

## Repository tool

The migration tool exposes:

- `migrate`;
- `validate`;
- `status`;
- `script`.

Run it through the PowerShell wrapper in `scripts/` so repository-root resolution and exit handling remain consistent with the Windows-only toolchain.

## Catalog rules

- Migration identities are ordered and immutable.
- Provider implementations must produce equivalent schema meaning.
- Historical upgrade paths are validated.
- Authorization, tenant ownership, token-hash versioning, refresh authentication time, reconciliation, and pagination indexes are part of the catalog.
- SQL Server and MySQL have no active migration implementation.

## Principals

Production deployment can separate a migration principal from a runtime principal. The migration principal owns controlled DDL; the runtime principal receives only the DML/read permissions and ledger access required by the host.

## References

- [Migration reference](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/MIGRATIONS.md)
- [Database schema](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/DATABASE.md)
- [Migration modes ADR](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/adr/0008-migration-modes-and-production-default.md)
- [Persistence and connection ownership](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/PERSISTENCE-AND-CONNECTIONS.md)
