using System.Collections.Concurrent;
using System.Globalization;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;

namespace SharpAccess.IntegrationTests;

public sealed class SqliteRecoveryDrillTests
{
    // Verifies that a checkpointed SQLite backup restores an intact verified account and login.
    [Fact]
    public async Task OfflineFileBackupRestoresVerifiedAccountAndLogin()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"sharpaccess-recovery-{Guid.NewGuid():N}");
        string databasePath = Path.Combine(directory, "sharpaccess.db");
        string backupPath = Path.Combine(directory, "sharpaccess.backup.db");
        const string email = "recovery@example.com";
        const string password = "ValidPassword123";
        RequestMetadata metadata = new("127.0.0.1", "recovery-drill");

        Directory.CreateDirectory(directory);
        try
        {
            CapturingEmailSender emails = new();
            using (ServiceProvider originalProvider = CreateProvider(databasePath, emails))
            {
                await originalProvider.InitializeSharpAccessAsync();

                await using AsyncServiceScope scope = originalProvider.CreateAsyncScope();
                IAuthService auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

                ServiceResult<string> registration = await auth.RegisterAsync(
                    email,
                    password,
                    metadata);
                Assert.True(registration.Succeeded);

                string verificationToken = ExtractFragmentToken(
                    Assert.Single(emails.Messages).TextBody,
                    "verify_token");
                Assert.True((await auth.VerifyEmailAsync(verificationToken, metadata)).Succeeded);
            }

            Assert.True(File.Exists(databasePath));
            await CheckpointAndVerifyAsync(databasePath);
            File.Copy(databasePath, backupPath, overwrite: true);
            File.Delete(databasePath);
            File.Copy(backupPath, databasePath, overwrite: true);
            await VerifyIntegrityAsync(databasePath);

            using ServiceProvider restoredProvider = CreateProvider(
                databasePath,
                new CapturingEmailSender());
            await restoredProvider.InitializeSharpAccessAsync();

            await using AsyncServiceScope restoredScope = restoredProvider.CreateAsyncScope();
            IAuthService restoredAuth =
                restoredScope.ServiceProvider.GetRequiredService<IAuthService>();
            ServiceResult<SessionTokens> login = await restoredAuth.LoginAsync(
                email,
                password,
                tenantId: null,
                metadata);

            Assert.True(login.Succeeded);
            Assert.NotNull(login.Value);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // Checkpoints WAL state and proves database integrity before the controlled file copy.
    private static async Task CheckpointAndVerifyAsync(string databasePath)
    {
        await using SqliteConnection connection = await OpenDatabaseAsync(databasePath);
        await using SqliteCommand mode = connection.CreateCommand();
        mode.CommandText = "PRAGMA journal_mode;";
        string journalMode = Convert.ToString(
            await mode.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture) ?? string.Empty;
        Assert.Equal("wal", journalMode, ignoreCase: true);

        await using SqliteCommand checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await using SqliteDataReader reader = await checkpoint.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        await reader.DisposeAsync();

        await VerifyIntegrityAsync(connection);
    }

    // Proves the restored file passes SQLite's full integrity check.
    private static async Task VerifyIntegrityAsync(string databasePath)
    {
        await using SqliteConnection connection = await OpenDatabaseAsync(databasePath);
        await VerifyIntegrityAsync(connection);
    }

    // Executes SQLite's full integrity check on an open connection.
    private static async Task VerifyIntegrityAsync(SqliteConnection connection)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        string result = Convert.ToString(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture) ?? string.Empty;
        Assert.Equal("ok", result, ignoreCase: true);
    }

    // Opens a non-pooled connection used only by the controlled recovery drill.
    private static async Task<SqliteConnection> OpenDatabaseAsync(string databasePath)
    {
        SqliteConnection connection = new($"Data Source={databasePath};Pooling=False");
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // Builds a provider with stable cryptographic settings and a file-backed SQLite database.
    private static ServiceProvider CreateProvider(
        string databasePath,
        CapturingEmailSender emails)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IEmailSender>(emails);
        services.AddSharpAccess(Configure);
        services.AddSqliteAccess(
            options => options.ConnectionString =
                $"Data Source={databasePath};Pooling=False;Cache=Private");
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Configures deterministic, low-cost security settings for the recovery drill.
    private static void Configure(AuthOptions options)
    {
        options.BaseUri = new Uri("https://recovery.test");
        options.JwtIssuer = "recovery-tests";
        options.JwtAudience = "recovery-clients";
        options.JwtSigningKey = "RECOVERY-JWT-SIGNING-KEY-12345678901234567890";
        options.Features.PasswordAuthentication = true;
        options.Features.Registration = true;
        options.Features.RefreshTokens = true;
        options.TokenHashing.Key = "RECOVERY-TOKEN-HASH-KEY-12345678901234567890";
        options.RateLimits.PartitionKey = "RECOVERY-RATE-LIMIT-KEY-12345678901234567890";
        options.Passwords.Iterations = 1;
        options.Passwords.MemorySizeKiB = 8_192;
        options.Passwords.DegreeOfParallelism = 1;
        options.Passwords.Peppers["v1"] =
            "RECOVERY-PASSWORD-PEPPER-12345678901234567890";
        options.RefreshCookieSecurePolicy = CookieSecurePolicy.Always;
        options.RefreshTokenCookieName = "__Secure-sharpaccess_refresh";
        options.RequireCsrfHeaderForCookieRefreshRequests = true;
        options.Migrations.Mode = SharpAccessMigrationMode.ApplyAtStartup;
    }

    // Extracts a single-use fragment token from a captured development email.
    private static string ExtractFragmentToken(string text, string name)
    {
        int marker = text.IndexOf($"#{name}=", StringComparison.Ordinal);
        Assert.True(marker >= 0);
        string encoded = text[(marker + name.Length + 2)..].Trim();
        return Uri.UnescapeDataString(encoded);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        internal ConcurrentBag<AuthEmailMessage> Messages { get; } = [];

        // Captures one email without logging its token-bearing body.
        public Task SendAsync(
            AuthEmailMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
