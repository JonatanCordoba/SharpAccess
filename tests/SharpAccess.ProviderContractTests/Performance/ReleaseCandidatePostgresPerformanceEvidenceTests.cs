using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpAccess;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using SharpAccess.ProviderContractTests;

namespace SharpAccess.ProviderContractTests.Performance;

[Trait("Provider", "Postgres")]
public sealed class ReleaseCandidatePostgresPerformanceEvidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    // Produces bounded PostgreSQL keyset-pagination and environment evidence on the opt-in scratch database.
    [PostgresFact]
    [Trait("Evidence", "Performance")]
    public async Task PostgresOperationsProduceReleaseCandidateEvidence()
    {
        string? outputDirectory = Environment.GetEnvironmentVariable("SHARPACCESS_PERFORMANCE_OUTPUT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        int iterations = ReadPositiveInteger("SHARPACCESS_PERFORMANCE_UNIT_ITERATIONS", 25, 2, 100);
        int warmupIterations = ReadPositiveInteger("SHARPACCESS_PERFORMANCE_WARMUP_ITERATIONS", 3, 1, 10);
        int requestedUsers = ReadPositiveInteger("SHARPACCESS_PERFORMANCE_POSTGRES_USERS", 225, 201, 500);
        int requestedMembers = ReadPositiveInteger("SHARPACCESS_PERFORMANCE_POSTGRES_MEMBERS", 225, 201, 500);
        int userCount = Math.Max(requestedUsers, requestedMembers + 1);

        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString).ConfigureAwait(false);
        await using ServiceProvider provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using IServiceScope scope = provider.CreateScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        await store.InitializeAsync().ConfigureAwait(false);

        AuthUser[] users = new AuthUser[userCount];
        for (int index = 0; index < users.Length; index++)
        {
            string email = FormattableString.Invariant($"performance-{index:D4}@example.test");
            DateTimeOffset createdUtc = Now.AddMilliseconds(index);
            AuthUser user = new(
                Guid.CreateVersion7(createdUtc),
                email,
                email.ToUpperInvariant(),
                "performance-password-hash",
                createdUtc,
                IsActive: true,
                FailedLoginAttempts: 0,
                LockoutEndUtc: null,
                SecurityVersion: 1,
                createdUtc,
                createdUtc);
            bool created = await store.CreateUserWithVerificationTokenAsync(
                user,
                CreateVerificationTokenHash(index),
                createdUtc.AddHours(1)).ConfigureAwait(false);
            Assert.True(created);
            users[index] = user;
        }

        TenantRecord? tenant = await store.CreateTenantAsync(
            "Performance tenant",
            "performance-tenant",
            users[0].Id,
            Now.AddMinutes(1)).ConfigureAwait(false);
        TenantRecord tenantRecord = tenant ?? throw new InvalidOperationException("The performance tenant was not created.");

        for (int index = 1; index <= requestedMembers; index++)
        {
            bool added = await store.AddTenantMemberAsync(
                tenantRecord.Id,
                users[index].Id,
                Now.AddMinutes(2).AddMilliseconds(index)).ConfigureAwait(false);
            Assert.True(added);
        }

        AuthPageSlice<AuthUser> firstUserPage = await store.ListUsersAsync(
            new AuthPageQuery(SharpAccessPageRequest.MaximumLimit, null)).ConfigureAwait(false);
        Assert.NotNull(firstUserPage.Next);
        AuthPageBoundary userBoundary = firstUserPage.Next
            ?? throw new InvalidOperationException("The PostgreSQL user dataset did not produce a continuation boundary.");

        AuthPageSlice<TenantMemberRecord> firstMemberPage = await store.ListTenantMembersAsync(
            tenantRecord.Id,
            new AuthPageQuery(SharpAccessPageRequest.MaximumLimit, null)).ConfigureAwait(false);
        Assert.NotNull(firstMemberPage.Next);
        AuthPageBoundary memberBoundary = firstMemberPage.Next
            ?? throw new InvalidOperationException("The PostgreSQL tenant-member dataset did not produce a continuation boundary.");

        for (int index = 0; index < warmupIterations; index++)
        {
            _ = await store.ListUsersAsync(
                new AuthPageQuery(SharpAccessPageRequest.MaximumLimit, userBoundary)).ConfigureAwait(false);
            _ = await store.ListTenantMembersAsync(
                tenantRecord.Id,
                new AuthPageQuery(SharpAccessPageRequest.MaximumLimit, memberBoundary)).ConfigureAwait(false);
        }

        PerformanceMetric usersMetric = await MeasureAsync(
            "postgres_user_keyset_page",
            iterations,
            async _ =>
            {
                AuthPageSlice<AuthUser> page = await store.ListUsersAsync(
                    new AuthPageQuery(SharpAccessPageRequest.MaximumLimit, userBoundary)).ConfigureAwait(false);
                Assert.NotEmpty(page.Items);
            }).ConfigureAwait(false);

        PerformanceMetric membersMetric = await MeasureAsync(
            "postgres_tenant_member_keyset_page",
            iterations,
            async _ =>
            {
                AuthPageSlice<TenantMemberRecord> page = await store.ListTenantMembersAsync(
                    tenantRecord.Id,
                    new AuthPageQuery(SharpAccessPageRequest.MaximumLimit, memberBoundary)).ConfigureAwait(false);
                Assert.NotEmpty(page.Items);
            }).ConfigureAwait(false);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using NpgsqlCommand configurationCommand = connection.CreateCommand();
        configurationCommand.CommandText = """
            SELECT
                current_setting('max_connections'),
                current_setting('shared_buffers'),
                current_setting('work_mem'),
                current_setting('effective_cache_size');
            """;
        await using NpgsqlDataReader configurationReader =
            await configurationCommand.ExecuteReaderAsync().ConfigureAwait(false);
        Assert.True(await configurationReader.ReadAsync().ConfigureAwait(false));

        var record = new
        {
            schemaVersion = 2,
            category = "postgresql-provider",
            datasetProfile = "scratch-postgres-keyset-users-and-tenant-members",
            datasetUserCount = users.Length,
            datasetTenantMemberCount = requestedMembers + 1,
            warmupIterations,
            postgresServerVersion = connection.PostgreSqlVersion.ToString(),
            postgresProviderVersion = typeof(NpgsqlConnection).Assembly.GetName().Version?.ToString() ?? "unknown",
            postgresConfiguration = new
            {
                maxConnections = configurationReader.GetString(0),
                sharedBuffers = configurationReader.GetString(1),
                workMem = configurationReader.GetString(2),
                effectiveCacheSize = configurationReader.GetString(3)
            },
            metrics = new[] { usersMetric, membersMetric },
            completedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        string json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "postgresql.json"),
            json).ConfigureAwait(false);
    }

    // Creates one non-secret deterministic hash value for provider setup.
    private static string CreateVerificationTokenHash(int index)
    {
        byte[] input = Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"performance-verification-{index:D4}"));
        try
        {
            return "v1:" + Convert.ToHexString(SHA256.HashData(input));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    // Measures one asynchronous provider operation without retaining row values or SQL.
    private static async Task<PerformanceMetric> MeasureAsync(
        string name,
        int iterations,
        Func<int, Task> operation)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        double[] samples = new double[iterations];

        for (int index = 0; index < iterations; index++)
        {
            long started = Stopwatch.GetTimestamp();
            await operation(index).ConfigureAwait(false);
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        long allocatedAfter = GC.GetTotalAllocatedBytes(true);
        long workingSetAfter = Process.GetCurrentProcess().WorkingSet64;
        Array.Sort(samples);
        int p95Index = Math.Clamp((int)Math.Ceiling(samples.Length * 0.95) - 1, 0, samples.Length - 1);
        return new PerformanceMetric(
            name,
            samples.Length,
            Math.Round(samples.Average(), 4),
            Math.Round(samples[samples.Length / 2], 4),
            Math.Round(samples[p95Index], 4),
            Math.Round(samples[^1], 4),
            Math.Max(0, (allocatedAfter - allocatedBefore) / samples.Length),
            workingSetAfter - workingSetBefore);
    }

    // Reads one bounded positive integer from the release-candidate environment.
    private static int ReadPositiveInteger(string name, int fallback, int minimum, int maximum)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
    }

    private sealed record PerformanceMetric(
        string Name,
        int Iterations,
        double MeanMilliseconds,
        double P50Milliseconds,
        double P95Milliseconds,
        double MaximumMilliseconds,
        long AllocatedBytesPerOperation,
        long WorkingSetDeltaBytes);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
