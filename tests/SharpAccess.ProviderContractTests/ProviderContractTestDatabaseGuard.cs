namespace SharpAccess.ProviderContractTests;

internal static class ProviderContractTestDatabaseGuard
{
    internal const string ResetPermissionEnvironmentVariable = "SHARPACCESS_PROVIDER_TEST_ALLOW_RESET";
    private const string ScratchDatabaseName = "sharpaccess_contract_tests";
    private const string ScratchDatabasePrefix = "sharpaccess_contract_tests_";

    // Reads a provider test connection string and proves that destructive reset is explicitly authorized.
    internal static string RequireResettableConnectionString(
        string providerName,
        string connectionStringEnvironmentVariable,
        Func<string, string?> databaseNameSelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(databaseNameSelector);

        string? connectionString = Environment.GetEnvironmentVariable(connectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{connectionStringEnvironmentVariable} is required for {providerName} provider tests.");
        }

        string? resetPermission = Environment.GetEnvironmentVariable(ResetPermissionEnvironmentVariable);
        if (!string.Equals(resetPermission, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ResetPermissionEnvironmentVariable}=true is required before {providerName} provider tests may reset auth tables.");
        }

        string? databaseName;
        try
        {
            databaseName = databaseNameSelector(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                $"{connectionStringEnvironmentVariable} is not a valid {providerName} connection string.",
                exception);
        }

        if (!IsApprovedScratchDatabaseName(databaseName))
        {
            throw new InvalidOperationException(
                $"{providerName} provider tests require database '{ScratchDatabaseName}' or a name beginning with '{ScratchDatabasePrefix}'. " +
                $"Configured database: '{databaseName ?? "<empty>"}'.");
        }

        return connectionString;
    }

    // Restricts destructive provider tests to clearly named, dedicated scratch databases.
    private static bool IsApprovedScratchDatabaseName(string? databaseName) =>
        string.Equals(databaseName, ScratchDatabaseName, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(databaseName)
            && databaseName.StartsWith(ScratchDatabasePrefix, StringComparison.OrdinalIgnoreCase));
}
