using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharpAccess;

return await MigrationTool.RunAsync(args).ConfigureAwait(false);

internal static class MigrationTool
{
    // Parses one repository migration-tool invocation and returns a stable process exit code.
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            MigrationToolArguments arguments = MigrationToolArguments.Parse(args);
            await using ServiceProvider services = BuildServices(arguments);
            switch (arguments.Command)
            {
                case "migrate":
                    await services.MigrateSharpAccessAsync().ConfigureAwait(false);
                    await WriteStatusAsync(services).ConfigureAwait(false);
                    return 0;
                case "validate":
                    await services.ValidateSharpAccessSchemaAsync().ConfigureAwait(false);
                    await WriteStatusAsync(services).ConfigureAwait(false);
                    return 0;
                case "status":
                    await WriteStatusAsync(services).ConfigureAwait(false);
                    return 0;
                case "script":
                    string script = await services.GenerateSharpAccessMigrationScriptAsync().ConfigureAwait(false);
                    if (arguments.OutputPath is null)
                    {
                        Console.Out.Write(script);
                    }
                    else
                    {
                        string fullPath = Path.GetFullPath(arguments.OutputPath);
                        string? directory = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        await File.WriteAllTextAsync(fullPath, script).ConfigureAwait(false);
                        Console.Out.WriteLine(fullPath);
                    }

                    return 0;
                default:
                    throw new ArgumentException($"Unsupported command: {arguments.Command}.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    // Creates a minimal SQLite service provider without registering application authentication features.
    private static ServiceProvider BuildServices(MigrationToolArguments arguments)
    {
        ServiceCollection services = new();
        services.AddSqliteAccess(options => options.ConnectionString = arguments.ConnectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Emits bounded machine-readable migration status without connection details.
    private static async Task WriteStatusAsync(IServiceProvider services)
    {
        SharpAccessSchemaStatus status = await services.GetSharpAccessSchemaStatusAsync().ConfigureAwait(false);
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            provider = status.ProviderName,
            current = status.IsCurrent,
            migrationLedger = status.MigrationLedgerExists,
            checksumLedger = status.ChecksumLedgerExists,
            applied = status.AppliedMigrations,
            pending = status.PendingMigrations,
            unknown = status.UnknownMigrations,
            missingChecksums = status.MissingChecksums,
            checksumMismatches = status.ChecksumMismatches
        }));
    }
}

internal sealed record MigrationToolArguments(
    string Command,
    string Provider,
    string ConnectionString,
    string? OutputPath)
{
    // Parses bounded named arguments and rejects unsupported providers before opening a connection.
    internal static MigrationToolArguments Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            throw new ArgumentException(
                "Usage: SharpAccess.MigrationTool <migrate|validate|status|script> --provider sqlite --connection <connection-string> [--output <path>]");
        }

        string command = args[0].ToLowerInvariant();
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Migration-tool options must use --name value pairs.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        string provider = Require(values, "provider").ToLowerInvariant();
        if (!string.Equals(provider, "sqlite", StringComparison.Ordinal))
        {
            throw new ArgumentException("The repository migration tool currently supports only the promoted SQLite provider.");
        }

        string connectionString = Require(values, "connection");
        if (connectionString.Length > 8_192)
        {
            throw new ArgumentException("The connection string is too long.");
        }

        string? outputPath = values.GetValueOrDefault("output");
        if (command == "script" && outputPath is { Length: > 4_096 })
        {
            throw new ArgumentException("The output path is too long.");
        }

        if (command is not ("migrate" or "validate" or "status" or "script"))
        {
            throw new ArgumentException($"Unsupported command: {command}.");
        }

        return new MigrationToolArguments(command, provider, connectionString, outputPath);
    }

    // Reads one required named value without echoing sensitive content.
    private static string Require(Dictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"--{name} is required.");
        }

        return value;
    }
}
