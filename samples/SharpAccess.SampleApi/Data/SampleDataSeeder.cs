using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SharpAccess.SampleApi;

internal sealed class SampleDataSeeder : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Action<ILogger, Exception?> LogSeedReady = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(1, nameof(LogSeedReady)),
        "SharpAccess sample data is ready.");
    private static readonly Action<ILogger, Exception?> LogSeedFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(2, nameof(LogSeedFailed)),
        "SharpAccess sample data seeding failed. The application remains available for manual testing.");
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly SampleMailbox _mailbox;
    private readonly ILogger<SampleDataSeeder> _logger;

    public SampleDataSeeder(
        IHostApplicationLifetime lifetime,
        IConfiguration configuration,
        IHostEnvironment environment,
        SampleMailbox mailbox,
        ILogger<SampleDataSeeder> logger)
    {
        _lifetime = lifetime;
        _configuration = configuration;
        _environment = environment;
        _mailbox = mailbox;
        _logger = logger;
    }

    internal static void ResetDatabaseIfRequested(WebApplicationBuilder builder)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SAMPLE_RESET_DATA"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string database = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "sample-auth.db");
        foreach (string path in new[] { database, database + "-shm", database + "-wal" })
        {
            File.Delete(path);
        }

        Console.WriteLine($"Reset sample database: {database}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_environment.IsDevelopment()
            || !_configuration.GetValue<bool>("SAMPLE_SEED_DEMO_DATA"))
        {
            return;
        }

        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = _lifetime.ApplicationStarted.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            started);
        await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(100, stoppingToken).ConfigureAwait(false);
            await SeedAsync(stoppingToken).ConfigureAwait(false);
            LogSeedReady(_logger, null);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogSeedFailed(_logger, exception);
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        Uri baseUri = new(_configuration["APP_BASE_URL"] ?? "http://localhost:5000", UriKind.Absolute);
        using HttpClient client = new(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };

        string adminEmail = Required("APP_SEED_ADMIN_EMAIL");
        string adminPassword = Required("APP_SEED_ADMIN_PASSWORD");
        string managerEmail = Required("SAMPLE_MANAGER_EMAIL");
        string managerPassword = Required("SAMPLE_MANAGER_PASSWORD");
        string userEmail = Required("SAMPLE_USER_EMAIL");
        string userPassword = Required("SAMPLE_USER_PASSWORD");

        await EnsureVerifiedUserAsync(client, managerEmail, managerPassword, cancellationToken).ConfigureAwait(false);
        await EnsureVerifiedUserAsync(client, userEmail, userPassword, cancellationToken).ConfigureAwait(false);

        TokenPayload admin = await LoginAsync(client, adminEmail, adminPassword, null, cancellationToken).ConfigureAwait(false);
        SetBearer(client, admin.AccessToken);
        IReadOnlyList<UserPayload> users = await GetPageAsync<UserPayload>(client, "/admin/users?limit=200", cancellationToken)
            .ConfigureAwait(false);
        UserPayload manager = users.Single(item => string.Equals(item.Email, managerEmail, StringComparison.OrdinalIgnoreCase));
        UserPayload standardUser = users.Single(item => string.Equals(item.Email, userEmail, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<PermissionPayload> permissions = await GetPageAsync<PermissionPayload>(
            client,
            "/admin/permissions?limit=200",
            cancellationToken).ConfigureAwait(false);
        Dictionary<string, PermissionPayload> permissionsByName = permissions.ToDictionary(
            static permission => permission.Name,
            StringComparer.Ordinal);

        List<RolePayload> roles = (await GetPageAsync<RolePayload>(client, "/admin/roles?limit=200", cancellationToken)
            .ConfigureAwait(false)).ToList();
        foreach (SampleModule module in SampleModuleCatalog.All)
        {
            RolePayload role = await EnsureRoleAsync(client, roles, module.RoleName, module.Description, cancellationToken)
                .ConfigureAwait(false);
            if (permissionsByName.TryGetValue(module.PermissionName, out PermissionPayload? permission))
            {
                await AcceptAsync(
                    client.PostAsJsonAsync(
                        $"/admin/roles/{role.Id:D}/permissions",
                        new { permissionId = permission.Id },
                        cancellationToken),
                    HttpStatusCode.NoContent,
                    HttpStatusCode.Conflict).ConfigureAwait(false);
            }
        }

        RolePayload managerRole = await EnsureRoleAsync(
            client,
            roles,
            "Sample Tenant Manager",
            "Testing role that can inspect users, roles, permissions, and tenants without full administration.",
            cancellationToken).ConfigureAwait(false);
        foreach (string permissionName in new[]
                 {
                     AuthPermissions.UsersRead,
                     AuthPermissions.RolesRead,
                     AuthPermissions.PermissionsRead,
                     AuthPermissions.TenantsRead
                 })
        {
            if (permissionsByName.TryGetValue(permissionName, out PermissionPayload? permission))
            {
                await AcceptAsync(
                    client.PostAsJsonAsync(
                        $"/admin/roles/{managerRole.Id:D}/permissions",
                        new { permissionId = permission.Id },
                        cancellationToken),
                    HttpStatusCode.NoContent,
                    HttpStatusCode.Conflict).ConfigureAwait(false);
            }
        }

        await AssignRoleAsync(client, manager.Id, managerRole.Id, cancellationToken).ConfigureAwait(false);
        foreach (SampleModule module in SampleModuleCatalog.All.Where(static item => item.Id is "users" or "tenants" or "roles"))
        {
            RolePayload moduleRole = roles.Single(role => string.Equals(role.Name, module.RoleName, StringComparison.Ordinal));
            await AssignRoleAsync(client, manager.Id, moduleRole.Id, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<TenantPayload> tenants = await GetPageAsync<TenantPayload>(client, "/tenants?limit=200", cancellationToken)
            .ConfigureAwait(false);
        TenantPayload firstTenant = await EnsureTenantAsync(
            client,
            tenants,
            "Northwind Test Lab",
            "northwind-test-lab",
            cancellationToken).ConfigureAwait(false);
        TenantPayload secondTenant = await EnsureTenantAsync(
            client,
            tenants,
            "Contoso Sandbox",
            "contoso-sandbox",
            cancellationToken).ConfigureAwait(false);

        foreach (TenantPayload tenant in new[] { firstTenant, secondTenant })
        {
            TokenPayload tenantAdmin = await LoginAsync(
                client,
                adminEmail,
                adminPassword,
                tenant.Id,
                cancellationToken).ConfigureAwait(false);
            SetBearer(client, tenantAdmin.AccessToken);
            await AddMemberAsync(client, tenant.Id, manager.Id, cancellationToken).ConfigureAwait(false);
            await AddMemberAsync(client, tenant.Id, standardUser.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureVerifiedUserAsync(
        HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage registration = await client.PostAsJsonAsync(
            "/auth/register",
            new { email, password },
            cancellationToken).ConfigureAwait(false);
        if (registration.StatusCode == HttpStatusCode.Accepted)
        {
            await VerifyNewestEmailAsync(client, email, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (registration.StatusCode != HttpStatusCode.Conflict)
        {
            await ThrowUnexpectedAsync(registration, "register sample user").ConfigureAwait(false);
        }

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password, tenantId = (Guid?)null },
            cancellationToken).ConfigureAwait(false);
        if (login.IsSuccessStatusCode)
        {
            return;
        }

        using HttpResponseMessage resend = await client.PostAsJsonAsync(
            "/auth/resend-verification",
            new { email },
            cancellationToken).ConfigureAwait(false);
        if (resend.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.NoContent)
        {
            await VerifyNewestEmailAsync(client, email, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ThrowUnexpectedAsync(login, "log in existing sample user").ConfigureAwait(false);
    }

    private async Task VerifyNewestEmailAsync(
        HttpClient client,
        string email,
        CancellationToken cancellationToken)
    {
        AuthEmailMessage message = await _mailbox.WaitForAsync(email, TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
        string token = ExtractFragmentValue(message.TextBody, "verify_token");
        await AcceptAsync(
            client.PostAsJsonAsync("/auth/verify-email", new { token }, cancellationToken),
            HttpStatusCode.NoContent).ConfigureAwait(false);
    }

    private static async Task<RolePayload> EnsureRoleAsync(
        HttpClient client,
        ICollection<RolePayload> roles,
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        RolePayload? existing = roles.FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/admin/roles",
            new { name, description },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        RolePayload created = await ReadAsync<RolePayload>(response, cancellationToken).ConfigureAwait(false);
        roles.Add(created);
        return created;
    }

    private static Task AssignRoleAsync(
        HttpClient client,
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken) =>
        AcceptAsync(
            client.PostAsJsonAsync(
                $"/admin/users/{userId:D}/roles",
                new { roleId },
                cancellationToken),
            HttpStatusCode.NoContent,
            HttpStatusCode.Conflict);

    private static async Task<TenantPayload> EnsureTenantAsync(
        HttpClient client,
        IReadOnlyList<TenantPayload> tenants,
        string name,
        string slug,
        CancellationToken cancellationToken)
    {
        TenantPayload? existing = tenants.FirstOrDefault(tenant => string.Equals(tenant.Slug, slug, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/tenants",
            new { name, slug },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<TenantPayload>(response, cancellationToken).ConfigureAwait(false);
    }

    private static Task AddMemberAsync(
        HttpClient client,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken) =>
        AcceptAsync(
            client.PostAsJsonAsync(
                $"/tenants/{tenantId:D}/members",
                new { userId },
                cancellationToken),
            HttpStatusCode.NoContent,
            HttpStatusCode.Conflict);

    private static async Task<TokenPayload> LoginAsync(
        HttpClient client,
        string email,
        string password,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password, tenantId },
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<TokenPayload>(response, cancellationToken).ConfigureAwait(false);
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<IReadOnlyList<T>> GetPageAsync<T>(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        PagePayload<T> page = await ReadAsync<PagePayload<T>>(response, cancellationToken).ConfigureAwait(false);
        return page.Items;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        T? value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException("A SharpAccess sample API response was empty.");
    }

    private static async Task AcceptAsync(
        Task<HttpResponseMessage> responseTask,
        params HttpStatusCode[] accepted)
    {
        using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
        if (!accepted.Contains(response.StatusCode))
        {
            await ThrowUnexpectedAsync(response, "apply sample seed mutation").ConfigureAwait(false);
        }
    }

    private static async Task ThrowUnexpectedAsync(HttpResponseMessage response, string operation)
    {
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Unable to {operation}; HTTP {(int)response.StatusCode}. {Bound(body, 512)}");
    }

    private static string ExtractFragmentValue(string textBody, string name)
    {
        int uriStart = textBody.LastIndexOf("http", StringComparison.Ordinal);
        if (uriStart < 0)
        {
            throw new InvalidOperationException("The sample verification email did not contain an absolute link.");
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

        throw new InvalidOperationException($"The sample verification email did not contain '{name}'.");
    }

    private string Required(string name) =>
        _configuration[name] ?? throw new InvalidOperationException($"{name} is required for sample data seeding.");

    private static string Bound(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private sealed record PagePayload<T>(IReadOnlyList<T> Items, string? NextCursor);
    private sealed record TokenPayload(string AccessToken);
    private sealed record UserPayload(Guid Id, string Email);
    private sealed record RolePayload(Guid Id, string Name, string Description, bool IsSystem);
    private sealed record PermissionPayload(Guid Id, string Name, string Description);
    private sealed record TenantPayload(Guid Id, string Name, string Slug, DateTimeOffset CreatedUtc);
}
