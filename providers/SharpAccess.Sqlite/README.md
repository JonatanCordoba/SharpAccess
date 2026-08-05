# SharpAccess.Sqlite

`SharpAccess.Sqlite` is the supported SQLite relational persistence provider package for SharpAccess.

The provider owns the SQLite dependency, connection creation, SQL dialect behavior, migrations, schema initialization, and SQLite-specific persistence. `SharpAccess.Core` remains provider-neutral and does not reference SQLite.

## Install and register

```bash
dotnet add package SharpAccess.Core
dotnet add package SharpAccess.Sqlite
```

```csharp
builder.Services.AddSharpAccess(builder.Configuration);
builder.Services.AddSqliteAccess(builder.Configuration);
```

```json
{
  "SharpAccess": {
    "Sqlite": {
      "ConnectionString": "Data Source=sharpaccess.db"
    }
  }
}
```

Install exactly one relational provider in a host. Do not register SQLite, PostgreSQL, SQL Server, and MySQL together.

## Operational behavior

Every provider connection enables foreign keys and a five-second busy timeout. Writable file-backed databases registered by connection string also require WAL journal mode and NORMAL synchronization. Host-managed connection factories retain ownership of journal, synchronization, pooling, and physical storage policy.

Protect the database, `-wal`, and `-shm` files with operating-system permissions. Do not use a network filesystem. SQLite has one concurrent writer; keep write transactions short and use a server provider when sustained write contention, failover, or horizontal scaling is required.

See [`docs/SQLITE.md`](../../docs/SQLITE.md) and [`docs/BACKUP-RESTORE.md`](../../docs/BACKUP-RESTORE.md) for the complete provider and recovery contract.

## Validation

```bash
bash ./scripts/sqlite-smoke.sh --repository-root "$PWD"
bash ./scripts/recovery-drill.sh --repository-root "$PWD"
```

```powershell
./scripts/sqlite-smoke.ps1 -RepositoryRoot $PWD
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

Provider tests use temporary local database files, verify keyset index plans and recovery integrity, clear pools, and remove their fixtures after each run.
