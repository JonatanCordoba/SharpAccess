# SQLite Provider

`SharpAccess.Sqlite` is the supported zero-infrastructure relational persistence package. Core remains provider-neutral.

## Install

```powershell
dotnet add package SharpAccess.Core --version 0.9.0-rc.1
dotnet add package SharpAccess.Sqlite --version 0.9.0-rc.1
```

## Register

```csharp
builder.Services.AddSharpAccess(builder.Configuration);
builder.Services.AddSqliteAccess(builder.Configuration);
```

Example provider configuration:

```json
{
  "SharpAccess": {
    "Sqlite": {
      "ConnectionString": "Data Source=sharpaccess.db"
    }
  }
}
```

## Connection policy

Each opened provider connection enables foreign keys and a bounded five-second busy timeout.

Writable file-backed databases created from the provider connection string also require:

- WAL journal mode;
- `synchronous=NORMAL`.

A host-managed connection factory retains ownership of journal mode, synchronization, pooling, and physical storage policy.

## Operational limits

SQLite has one concurrent writer. Keep transactions short, do not place the database on a network filesystem, and use PostgreSQL when sustained write contention, failover, multiple writers, or horizontal scaling require a server DB.

Protect the database, `-wal`, and `-shm` files with operating-system permissions. Do not place them under a static-content root or a shared user profile.

## Validation

```powershell
./scripts/sqlite-smoke.ps1 -RepositoryRoot $PWD
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

## Recovery

A safe controlled backup checkpoints WAL, verifies integrity, copies the database while quiesced, restores it, verifies integrity again, and proves a verified account can sign in. See [Recovery](Recovery).

## References

- [SQLite provider reference](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/SQLITE.md)
- [Provider package README](https://github.com/JonatanCordoba/SharpAccess/blob/main/providers/SharpAccess.Sqlite/README.md)
- [Backup and restore](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/BACKUP-RESTORE.md)
