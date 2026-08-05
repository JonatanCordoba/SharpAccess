namespace SharpAccess;

/// <summary>Describes the provider-owned SharpAccess schema without exposing database-specific metadata.</summary>
public sealed class SharpAccessSchemaStatus
{
    internal SharpAccessSchemaStatus(
        string providerName,
        bool migrationLedgerExists,
        bool checksumLedgerExists,
        IEnumerable<string> appliedMigrations,
        IEnumerable<string> pendingMigrations,
        IEnumerable<string> unknownMigrations,
        IEnumerable<string> missingChecksums,
        IEnumerable<string> checksumMismatches)
    {
        ProviderName = providerName;
        MigrationLedgerExists = migrationLedgerExists;
        ChecksumLedgerExists = checksumLedgerExists;
        AppliedMigrations = appliedMigrations.ToArray();
        PendingMigrations = pendingMigrations.ToArray();
        UnknownMigrations = unknownMigrations.ToArray();
        MissingChecksums = missingChecksums.ToArray();
        ChecksumMismatches = checksumMismatches.ToArray();
    }

    /// <summary>Gets the stable provider identifier.</summary>
    public string ProviderName { get; }

    /// <summary>Gets whether the migration ledger exists.</summary>
    public bool MigrationLedgerExists { get; }

    /// <summary>Gets whether immutable migration checksums are tracked.</summary>
    public bool ChecksumLedgerExists { get; }

    /// <summary>Gets applied migration identifiers in deterministic order.</summary>
    public IReadOnlyList<string> AppliedMigrations { get; }

    /// <summary>Gets known migrations that still need to be applied.</summary>
    public IReadOnlyList<string> PendingMigrations { get; }

    /// <summary>Gets applied migration identifiers that are absent from the provider catalog.</summary>
    public IReadOnlyList<string> UnknownMigrations { get; }

    /// <summary>Gets applied migrations that have not yet received a checksum baseline.</summary>
    public IReadOnlyList<string> MissingChecksums { get; }

    /// <summary>Gets applied migrations whose stored checksum differs from the immutable provider catalog.</summary>
    public IReadOnlyList<string> ChecksumMismatches { get; }

    /// <summary>Gets whether validation can prove that the schema matches the current provider catalog.</summary>
    public bool IsCurrent =>
        MigrationLedgerExists
        && ChecksumLedgerExists
        && PendingMigrations.Count == 0
        && UnknownMigrations.Count == 0
        && MissingChecksums.Count == 0
        && ChecksumMismatches.Count == 0;
}
