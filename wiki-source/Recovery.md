# Recovery

Recovery is provider-specific and must be exercised with the host’s real retention, encryption, storage, RPO, and RTO requirements.

## SQLite

The controlled repository drill:

1. quiesces the test host;
2. runs `wal_checkpoint(TRUNCATE)`;
3. verifies `PRAGMA integrity_check`;
4. copies the main database file;
5. restores it;
6. verifies integrity again;
7. proves a verified account can sign in.

Run:

```powershell
./scripts/recovery-drill.ps1 -RepositoryRoot $PWD
```

A raw copy of only the main SQLite file is unsafe while writes or uncheckpointed WAL frames may exist.

## PostgreSQL

The native recovery contract uses approved Windows tooling and an approved scratch database. It validates logical backup and restore, schema state, restricted operation, and authentication behavior.

Run the repository’s current command:

```powershell
./scripts/postgres-recovery-drill.ps1 -RepositoryRoot $PWD
```

The connection string must remain protected and absent from retained evidence.

## Host responsibilities

- Encrypt and restrict backups.
- Back up Data Protection keys with the application DB when protected payload continuity is required.
- Test restore, not only backup creation.
- Document retention, RPO, RTO, operator, reviewer, and completion evidence.
- Treat recovery failures as release or operational blockers.

## References

- [Backup and restore](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/BACKUP-RESTORE.md)
- [Business continuity](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/BUSINESS-CONTINUITY.md)
- [PostgreSQL operations](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/POSTGRES-OPERATIONS.md)
- [Recovery-drill template](https://github.com/JonatanCordoba/SharpAccess/blob/main/docs/templates/RECOVERY-DRILL.md)
