using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SharpAccess.EndpointTests.Performance;

public sealed class ReleaseCandidateEndpointPerformanceEvidenceTests :
    IClassFixture<AuthenticationEndpointTests.AuthApplicationFactory>
{
    private readonly AuthenticationEndpointTests.AuthApplicationFactory _factory;

    // Stores the isolated application factory used by endpoint performance evidence.
    public ReleaseCandidateEndpointPerformanceEvidenceTests(
        AuthenticationEndpointTests.AuthApplicationFactory factory)
    {
        _factory = factory;
    }

    // Produces bounded end-to-end authentication, persistence, pagination, tenancy, and invalidation measurements.
    [Fact]
    [Trait("Evidence", "Performance")]
    public async Task EndpointOperationsProduceReleaseCandidateEvidence()
    {
        string? outputDirectory = Environment.GetEnvironmentVariable("SHARPACCESS_PERFORMANCE_OUTPUT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        int iterations = ReadPositiveInteger("SHARPACCESS_PERFORMANCE_ENDPOINT_ITERATIONS", 3, 2, 3);
        int warmupIterations = ReadPositiveInteger("SHARPACCESS_PERFORMANCE_WARMUP_ITERATIONS", 3, 1, 10);
        using HttpClient globalClient = _factory.CreateClient();

        TokenPayload globalSession = await LoginAsync(globalClient, tenantId: null);
        globalClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", globalSession.AccessToken);

        string benchmarkEmail = $"performance-{Guid.NewGuid():N}@test.local";
        Guid benchmarkUserId = await RegisterAndResolveUserIdAsync(globalClient, benchmarkEmail);
        Guid managerRoleId = await ResolveRoleIdAsync(globalClient, "Manager");

        List<string> refreshTokens = [];
        for (int index = 0; index < warmupIterations; index++)
        {
            TokenPayload session = await LoginAsync(globalClient, tenantId: null);
            refreshTokens.Add(session.RefreshToken
                ?? throw new InvalidOperationException("Refresh token response-body transport is required for performance evidence."));
        }

        PerformanceMetric login = await MeasureAsync(
            "endpoint_login",
            iterations,
            async iteration =>
            {
                TokenPayload session = await LoginAsync(globalClient, tenantId: null);
                refreshTokens.Add(session.RefreshToken
                    ?? throw new InvalidOperationException("Refresh token response-body transport is required for performance evidence."));
            });
        List<string> replayTokens = [];
        for (int index = 0; index < warmupIterations; index++)
        {
            using HttpResponseMessage response = await globalClient.PostAsJsonAsync("/auth/refresh", new
            {
                refreshToken = refreshTokens[index],
                tenantId = (Guid?)null
            });
            response.EnsureSuccessStatusCode();
            TokenPayload rotated = await ReadTokenAsync(response);
            replayTokens.Add(rotated.RefreshToken
                ?? throw new InvalidOperationException("Refresh token replay evidence requires response-body transport."));
        }

        PerformanceMetric refresh = await MeasureAsync(
            "endpoint_refresh_rotation",
            iterations,
            async index =>
            {
                using HttpResponseMessage response = await globalClient.PostAsJsonAsync("/auth/refresh", new
                {
                    refreshToken = refreshTokens[warmupIterations + index],
                    tenantId = (Guid?)null
                });
                response.EnsureSuccessStatusCode();
                TokenPayload rotated = await ReadTokenAsync(response);
                replayTokens.Add(rotated.RefreshToken
                    ?? throw new InvalidOperationException("Refresh token replay evidence requires response-body transport."));
            });

        for (int index = 0; index < warmupIterations; index++)
        {
            await RunRefreshReplayContentionAsync(globalClient, replayTokens[index]);
        }

        PerformanceMetric replayContention = await MeasureAsync(
            "endpoint_refresh_replay_contention",
            iterations,
            index => RunRefreshReplayContentionAsync(
                globalClient,
                replayTokens[warmupIterations + index]));

        for (int index = 0; index < warmupIterations; index++)
        {
            using HttpResponseMessage response = await globalClient.GetAsync("/auth/me");
            response.EnsureSuccessStatusCode();
        }

        PerformanceMetric persistedState = await MeasureAsync(
            "endpoint_persisted_state_validation",
            Math.Max(10, iterations * 10),
            async _ =>
            {
                using HttpResponseMessage response = await globalClient.GetAsync("/auth/me");
                response.EnsureSuccessStatusCode();
            });

        for (int index = 0; index < warmupIterations; index++)
        {
            using HttpResponseMessage response = await globalClient.GetAsync("/admin/users?limit=200");
            response.EnsureSuccessStatusCode();
        }

        PerformanceMetric pagination = await MeasureAsync(
            "endpoint_user_keyset_page",
            Math.Max(10, iterations * 10),
            async _ =>
            {
                using HttpResponseMessage response = await globalClient.GetAsync("/admin/users?limit=200");
                response.EnsureSuccessStatusCode();
            });

        PerformanceMetric roleInvalidation = await MeasureAsync(
            "endpoint_role_invalidation_cycle",
            iterations,
            async _ =>
            {
                using HttpResponseMessage assign = await globalClient.PostAsJsonAsync(
                    $"/admin/users/{benchmarkUserId:D}/roles",
                    new { roleId = managerRoleId });
                assign.EnsureSuccessStatusCode();

                using HttpResponseMessage remove = await globalClient.DeleteAsync(
                    $"/admin/users/{benchmarkUserId:D}/roles/{managerRoleId:D}");
                remove.EnsureSuccessStatusCode();
            });

        Guid tenantId = await CreateTenantAsync(globalClient);
        using HttpClient tenantClient = _factory.CreateClient();
        TokenPayload tenantSession = await LoginAsync(tenantClient, tenantId);
        tenantClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tenantSession.AccessToken);
        using (HttpResponseMessage addMember = await tenantClient.PostAsJsonAsync(
            $"/tenants/{tenantId:D}/members",
            new { userId = benchmarkUserId }))
        {
            addMember.EnsureSuccessStatusCode();
        }

        for (int index = 0; index < warmupIterations; index++)
        {
            using HttpResponseMessage response = await tenantClient.GetAsync(
                $"/tenants/{tenantId:D}/members?limit=200");
            response.EnsureSuccessStatusCode();
        }

        PerformanceMetric tenantMembers = await MeasureAsync(
            "endpoint_tenant_member_page",
            Math.Max(10, iterations * 10),
            async _ =>
            {
                using HttpResponseMessage response = await tenantClient.GetAsync(
                    $"/tenants/{tenantId:D}/members?limit=200");
                response.EnsureSuccessStatusCode();
            });

        string sqliteNativeVersion = await ReadSqliteNativeVersionAsync();
        var record = new
        {
            schemaVersion = 2,
            category = "endpoint-and-sqlite",
            datasetProfile = "isolated-sqlite-admin-plus-one-member",
            datasetUserCount = 2,
            datasetTenantMemberCount = 2,
            warmupIterations,
            tokenSizeBytes = globalSession.AccessToken.Length,
            sqliteProviderVersion = typeof(SqliteConnection).Assembly.GetName().Version?.ToString() ?? "unknown",
            sqliteNativeVersion,
            metrics = new[]
            {
                login,
                refresh,
                replayContention,
                persistedState,
                pagination,
                roleInvalidation,
                tenantMembers
            },
            completedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        string json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "endpoints.json"), json);
    }

    // Registers and verifies one user, then resolves its stable identifier through bounded administration pagination.
    private async Task<Guid> RegisterAndResolveUserIdAsync(HttpClient adminClient, string email)
    {
        const string password = "Performance123!Sample";
        using HttpClient anonymousClient = _factory.CreateClient();
        using (HttpResponseMessage registration = await anonymousClient.PostAsJsonAsync(
            "/auth/register",
            new { email, password }))
        {
            Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        }

        AuthEmailMessage message = await _factory.Emails.WaitForAsync(email, TimeSpan.FromSeconds(10));
        string token = ExtractFragmentValue(message.TextBody, "verify_token");
        using (HttpResponseMessage verification = await anonymousClient.PostAsJsonAsync(
            "/auth/verify-email",
            new { token }))
        {
            Assert.Equal(HttpStatusCode.NoContent, verification.StatusCode);
        }

        using HttpResponseMessage users = await adminClient.GetAsync("/admin/users?limit=200");
        users.EnsureSuccessStatusCode();
        using JsonDocument document = await JsonDocument.ParseAsync(await users.Content.ReadAsStreamAsync());
        foreach (JsonElement item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (string.Equals(item.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase))
            {
                return item.GetProperty("id").GetGuid();
            }
        }

        throw new InvalidOperationException("The benchmark user was not returned by bounded administration pagination.");
    }

    // Resolves one global role identifier through the bounded role catalog.
    private static async Task<Guid> ResolveRoleIdAsync(HttpClient client, string roleName)
    {
        using HttpResponseMessage roles = await client.GetAsync("/admin/roles?limit=200");
        roles.EnsureSuccessStatusCode();
        using JsonDocument document = await JsonDocument.ParseAsync(await roles.Content.ReadAsStreamAsync());
        foreach (JsonElement item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (string.Equals(item.GetProperty("name").GetString(), roleName, StringComparison.Ordinal))
            {
                return item.GetProperty("id").GetGuid();
            }
        }

        throw new InvalidOperationException($"Global role was not found: {roleName}");
    }

    // Creates one isolated tenant owned by the seeded administrator.
    private static async Task<Guid> CreateTenantAsync(HttpClient client)
    {
        string suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using HttpResponseMessage response = await client.PostAsJsonAsync("/tenants", new
        {
            name = $"Performance {suffix}",
            slug = $"performance-{suffix}"
        });
        response.EnsureSuccessStatusCode();
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    // Logs in one verified account for global or tenant-scoped measurements.
    private static async Task<TokenPayload> LoginAsync(
        HttpClient client,
        Guid? tenantId,
        string email = "admin@test.local",
        string password = "Admin123!Sample")
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password,
            tenantId
        });
        response.EnsureSuccessStatusCode();
        return await ReadTokenAsync(response);
    }

    // Parses one successful token response without retaining caller-owned response state.
    private static async Task<TokenPayload> ReadTokenAsync(HttpResponseMessage response)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        TokenPayload? payload = await JsonSerializer.DeserializeAsync<TokenPayload>(stream, JsonOptions);
        return payload ?? throw new InvalidOperationException("The token response was empty.");
    }

    // Extracts and decodes one fragment value from a verification message.
    private static string ExtractFragmentValue(string textBody, string name)
    {
        int uriStart = textBody.LastIndexOf("http", StringComparison.Ordinal);
        if (uriStart < 0)
        {
            throw new InvalidOperationException("The verification message did not contain an absolute link.");
        }

        Uri uri = new(textBody[uriStart..]);
        foreach (string pair in uri.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"The verification fragment did not contain '{name}'.");
    }


    // Executes two concurrent refresh attempts with one token and requires exactly one success.
    private static async Task RunRefreshReplayContentionAsync(
        HttpClient client,
        string refreshToken)
    {
        Task<HttpResponseMessage>[] requests =
        [
            client.PostAsJsonAsync("/auth/refresh", new
            {
                refreshToken,
                tenantId = (Guid?)null
            }),
            client.PostAsJsonAsync("/auth/refresh", new
            {
                refreshToken,
                tenantId = (Guid?)null
            })
        ];
        HttpResponseMessage[] responses = await Task.WhenAll(requests);
        try
        {
            Assert.Equal(1, responses.Count(static response => response.IsSuccessStatusCode));
            Assert.All(responses, static response =>
                Assert.True(
                    (int)response.StatusCode < 500,
                    $"Refresh replay contention returned server error {(int)response.StatusCode}."));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    // Reads the native SQLite version through the same provider package used by endpoint evidence.
    private static async Task<string> ReadSqliteNativeVersionAsync()
    {
        SQLitePCL.Batteries_V2.Init();
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        object? result = await command.ExecuteScalarAsync();
        return Convert.ToString(result, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("SQLite did not report a native version.");
    }
    // Measures one asynchronous endpoint operation without retaining credentials or tokens.
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
            await operation(index);
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

    private sealed record TokenPayload(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresUtc,
        string TokenType,
        string? RefreshToken,
        DateTimeOffset? RefreshTokenExpiresUtc);

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
