# SQLite provider

`SharpAccess.Sqlite` is the supported SQLite relational persistence provider for SharpAccess and remains the default zero-infrastructure path for the sample, local development, deterministic tests, and clean-clone verification. It is a separate provider package; `SharpAccess.Core` remains provider-neutral.

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

Install exactly one relational persistence provider in a host. `SqliteAccess` remains a staged configuration fallback, but new hosts should use `SharpAccess:Sqlite`.

## Reference operational profile

SharpAccess applies these settings on every opened SQLite connection:

- foreign-key enforcement enabled;
- a bounded `5000` millisecond busy timeout.

For writable file-backed databases created from `SqliteAuthOptions.ConnectionString`, SharpAccess also requires:

- write-ahead logging (`journal_mode=WAL`);
- `synchronous=NORMAL`.

This profile balances crash resilience and practical concurrent read/write behavior for the default local and small-host deployment. It does not turn SQLite into a horizontally scalable server database. Keep transactions short, avoid network filesystems, and move to a server provider when write contention or availability requirements exceed SQLite's single-writer model.

A host-managed connection factory retains ownership of journal mode, synchronization, pooling, and connection-lifetime policy. SharpAccess still enables foreign keys and the bounded busy timeout on each logical connection, but does not silently replace host file-level policy.

## File and secret protection

Create the database in a directory writable only by the application identity. Restrict the database, `-wal`, and `-shm` files using operating-system permissions. Do not place the database under a static-content root, shared user profile, source checkout, or world-readable temporary directory.

JWT signing keys, token-hashing keys, and password peppers are not database contents. Back them up and rotate them under the host secret-management policy.

## Backup and recovery

Use the controlled procedure in [BACKUP-RESTORE.md](BACKUP-RESTORE.md). A raw copy of only the main database file is unsafe while writes or uncheckpointed WAL frames may exist. The repository recovery drill quiesces the test host, runs `wal_checkpoint(TRUNCATE)`, verifies `PRAGMA integrity_check`, copies the main file, restores it, verifies integrity again, and proves a verified account can log in.

```bash
bash ./scripts/recovery-drill.sh --repository-root "$PWD"
```

```powershell
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

The package drill is controlled evidence, not a substitute for a host-specific backup, restore, retention, encryption, and disaster-recovery exercise.

## Validation

```bash
bash ./scripts/sqlite-smoke.sh --repository-root "$PWD"
```

```powershell
./scripts/sqlite-smoke.ps1 -RepositoryRoot $PWD
```

The provider-contract suite covers connection policy, transactions, bounded keyset pagination, migration ordering, query-plan index references, recovery, and provider-neutral behavior. Temporary local database files are removed after each test run and pools are cleared before cleanup.
