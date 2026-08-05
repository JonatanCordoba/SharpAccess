using Microsoft.Extensions.Options;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed class PostgresAuthOptionsValidator : IValidateOptions<PostgresAuthOptions>
{
    private const int MaximumConnectionTimeoutSeconds = 60;
    private const int MaximumCommandTimeoutSeconds = 300;
    private const int MaximumCancellationTimeoutMilliseconds = 10_000;
    private const int MaximumPoolSize = 500;

    // Validates bounded and non-sensitive PostgreSQL connection behavior.
    public ValidateOptionsResult Validate(string? name, PostgresAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail("PostgresAuthOptions.ConnectionString is required.");
        }

        try
        {
            NpgsqlConnectionStringBuilder builder = new(options.ConnectionString);
            return ValidateBuilder(builder);
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("PostgresAuthOptions.ConnectionString is malformed.");
        }
    }

    // Applies the PostgreSQL operational safety policy to a parsed connection string.
    private static ValidateOptionsResult ValidateBuilder(NpgsqlConnectionStringBuilder builder)
    {
        List<string> failures = [];
        RequireText(builder.Host, "Host", failures);
        RequireText(builder.Database, "Database", failures);
        RequireRange(builder.Timeout, 1, MaximumConnectionTimeoutSeconds, "Timeout", failures);
        RequireRange(builder.CommandTimeout, 1, MaximumCommandTimeoutSeconds, "Command Timeout", failures);
        RequireRange(builder.CancellationTimeout, 1, MaximumCancellationTimeoutMilliseconds, "Cancellation Timeout", failures);
        if (builder.Pooling)
        {
            RequireRange(builder.MaxPoolSize, 1, MaximumPoolSize, "Maximum Pool Size", failures);
            if (builder.MinPoolSize < 0 || builder.MinPoolSize > builder.MaxPoolSize)
            {
                failures.Add("Minimum Pool Size must be nonnegative and no greater than Maximum Pool Size.");
            }
        }

        RejectEnabled(builder.IncludeErrorDetail, "Include Error Detail", failures);
        RejectEnabled(builder.LogParameters, "Log Parameters", failures);
        RejectEnabled(builder.IncludeFailedBatchedCommand, "Include Failed Batched Command", failures);
        RejectEnabled(builder.NoResetOnClose, "No Reset On Close", failures);
        RejectEnabled(builder.Multiplexing, "Multiplexing", failures);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    // Requires one non-empty parsed connection-string value.
    private static void RequireText(string? value, string label, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{label} is required.");
        }
    }

    // Requires one bounded positive connection setting.
    private static void RequireRange(int value, int minimum, int maximum, string label, List<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{label} must be between {minimum} and {maximum}.");
        }
    }

    // Rejects a diagnostic or pooling setting that weakens secret handling or connection isolation.
    private static void RejectEnabled(bool enabled, string label, List<string> failures)
    {
        if (enabled)
        {
            failures.Add($"{label} must remain disabled for SharpAccess PostgreSQL persistence.");
        }
    }
}
