using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SharpAccess.EndpointTests;

public sealed class AuthorizationScopeEndpointTests :
    IClassFixture<AuthenticationEndpointTests.AuthApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthenticationEndpointTests.AuthApplicationFactory _factory;

    public AuthorizationScopeEndpointTests(AuthenticationEndpointTests.AuthApplicationFactory factory) =>
        _factory = factory;

    // Verifies that tenant authorization cannot satisfy administration policies or cross tenant routes.
    [Fact]
    public async Task TenantMemberCannotUseTenantClaimsForGlobalAdministrationOrAnotherTenant()
    {
        using HttpClient client = _factory.CreateClient();
        TokenPayload globalAdmin = await LoginAsync(
            client,
            "admin@test.local",
            "Admin123!Sample",
            tenantId: null);
        client.DefaultRequestHeaders.Authorization = Bearer(globalAdmin.AccessToken);

        string memberEmail = $"scope-member-{Guid.NewGuid():N}@test.local";
        const string memberPassword = "Member123!Sample";
        await RegisterAndVerifyAsync(client, memberEmail, memberPassword);

        using HttpResponseMessage usersResponse = await client.GetAsync("/admin/users");
        usersResponse.EnsureSuccessStatusCode();
        PagePayload<UserPayload> users = await ReadAsync<PagePayload<UserPayload>>(usersResponse);
        Guid memberId = Assert.Single(users.Items, user =>
            string.Equals(user.Email, memberEmail, StringComparison.OrdinalIgnoreCase)).Id;

        string slug = $"scope-{Guid.NewGuid():N}";
        using HttpResponseMessage createTenantResponse = await client.PostAsJsonAsync(
            "/tenants/",
            new { name = "Authorization Scope Tenant", slug });
        Assert.Equal(HttpStatusCode.Created, createTenantResponse.StatusCode);
        TenantPayload tenant = await ReadAsync<TenantPayload>(createTenantResponse);

        TokenPayload ownerSession = await LoginAsync(
            client,
            "admin@test.local",
            "Admin123!Sample",
            tenant.Id);
        client.DefaultRequestHeaders.Authorization = Bearer(ownerSession.AccessToken);
        using HttpResponseMessage addMemberResponse = await client.PostAsJsonAsync(
            $"/tenants/{tenant.Id:D}/members",
            new { userId = memberId });
        Assert.Equal(HttpStatusCode.NoContent, addMemberResponse.StatusCode);

        TokenPayload tenantMember = await LoginAsync(
            client,
            memberEmail,
            memberPassword,
            tenant.Id);
        client.DefaultRequestHeaders.Authorization = Bearer(tenantMember.AccessToken);

        using HttpResponseMessage adminResponse = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);

        using HttpResponseMessage ownTenantResponse = await client.GetAsync($"/tenants/{tenant.Id:D}");
        Assert.Equal(HttpStatusCode.OK, ownTenantResponse.StatusCode);

        using HttpResponseMessage otherTenantResponse = await client.GetAsync(
            $"/tenants/{Guid.NewGuid():D}/members");
        Assert.Equal(HttpStatusCode.Forbidden, otherTenantResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(globalAdmin.AccessToken);
        using HttpResponseMessage globalReadResponse = await client.GetAsync($"/tenants/{tenant.Id:D}");
        Assert.Equal(HttpStatusCode.OK, globalReadResponse.StatusCode);
    }

    // Verifies that the owner-only transfer route rejects a tenant member token.
    [Fact]
    public async Task NonOwnerTenantMemberCannotTransferOwnership()
    {
        using HttpClient client = _factory.CreateClient();
        TokenPayload globalAdmin = await LoginAsync(
            client,
            "admin@test.local",
            "Admin123!Sample",
            tenantId: null);
        client.DefaultRequestHeaders.Authorization = Bearer(globalAdmin.AccessToken);

        string memberEmail = $"transfer-member-{Guid.NewGuid():N}@test.local";
        const string memberPassword = "Member123!Sample";
        await RegisterAndVerifyAsync(client, memberEmail, memberPassword);
        using HttpResponseMessage usersResponse = await client.GetAsync("/admin/users");
        usersResponse.EnsureSuccessStatusCode();
        PagePayload<UserPayload> users = await ReadAsync<PagePayload<UserPayload>>(usersResponse);
        Guid memberId = Assert.Single(users.Items, user =>
            string.Equals(user.Email, memberEmail, StringComparison.OrdinalIgnoreCase)).Id;

        using HttpResponseMessage createTenantResponse = await client.PostAsJsonAsync(
            "/tenants/",
            new
            {
                name = "Ownership Boundary Tenant",
                slug = $"ownership-{Guid.NewGuid():N}"
            });
        createTenantResponse.EnsureSuccessStatusCode();
        TenantPayload tenant = await ReadAsync<TenantPayload>(createTenantResponse);

        TokenPayload ownerSession = await LoginAsync(
            client,
            "admin@test.local",
            "Admin123!Sample",
            tenant.Id);
        client.DefaultRequestHeaders.Authorization = Bearer(ownerSession.AccessToken);
        using HttpResponseMessage addMemberResponse = await client.PostAsJsonAsync(
            $"/tenants/{tenant.Id:D}/members",
            new { userId = memberId });
        Assert.Equal(HttpStatusCode.NoContent, addMemberResponse.StatusCode);

        TokenPayload memberSession = await LoginAsync(
            client,
            memberEmail,
            memberPassword,
            tenant.Id);
        client.DefaultRequestHeaders.Authorization = Bearer(memberSession.AccessToken);
        using HttpResponseMessage transferResponse = await client.PostAsJsonAsync(
            $"/tenants/{tenant.Id:D}/owner/transfer",
            new { newOwnerUserId = memberId });

        Assert.Equal(HttpStatusCode.Forbidden, transferResponse.StatusCode);
    }

    private async Task RegisterAndVerifyAsync(
        HttpClient client,
        string email,
        string password)
    {
        AuthenticationHeaderValue? callerAuthorization = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = null;
        try
        {
            using HttpResponseMessage registration = await client.PostAsJsonAsync(
                "/auth/register",
                new { email, password });
            Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);

            AuthEmailMessage message = await _factory.Emails.WaitForAsync(email, TimeSpan.FromSeconds(5));
            string token = ExtractFragmentValue(message.TextBody, "verify_token");
            using HttpResponseMessage verification = await client.PostAsJsonAsync(
                "/auth/verify-email",
                new { token });
            Assert.Equal(HttpStatusCode.NoContent, verification.StatusCode);
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = callerAuthorization;
        }
    }

    private static async Task<TokenPayload> LoginAsync(
        HttpClient client,
        string email,
        string password,
        Guid? tenantId)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/login",
            new { email, password, tenantId });
        response.EnsureSuccessStatusCode();
        return await ReadAsync<TokenPayload>(response);
    }

    private static AuthenticationHeaderValue Bearer(string accessToken) =>
        new("Bearer", accessToken);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        T? value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
        return value ?? throw new InvalidOperationException("The response payload was empty.");
    }

    private static string ExtractFragmentValue(string textBody, string name)
    {
        int uriStart = textBody.LastIndexOf("http", StringComparison.Ordinal);
        Assert.True(uriStart >= 0, "The email body did not contain an absolute link.");
        Uri uri = new(textBody[uriStart..]);
        foreach (string pair in uri.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"The fragment value '{name}' was not present.");
    }

    private sealed record TokenPayload(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresUtc,
        string TokenType,
        string? RefreshToken,
        DateTimeOffset? RefreshTokenExpiresUtc);

    private sealed record UserPayload(Guid Id, string Email);

    private sealed record PagePayload<T>(IReadOnlyList<T> Items, string? NextCursor);

    private sealed record TenantPayload(Guid Id, string Name, string Slug, DateTimeOffset CreatedUtc);
}
