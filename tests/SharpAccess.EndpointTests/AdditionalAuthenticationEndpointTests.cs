using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SharpAccess.EndpointTests;

public sealed class AdditionalAuthenticationEndpointTests : IClassFixture<AuthenticationEndpointTests.AuthApplicationFactory>
{
    private readonly AuthenticationEndpointTests.AuthApplicationFactory _factory;

    public AdditionalAuthenticationEndpointTests(AuthenticationEndpointTests.AuthApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PasswordResetEndpointAcceptsIssuedTokenAndChangesCredentials()
    {
        using HttpClient client = _factory.CreateClient();
        string email = $"reset-{Guid.NewGuid():N}@test.local";
        const string originalPassword = "Original123!Sample";
        const string replacementPassword = "Replacement123!Sample";

        using HttpResponseMessage registration = await client.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = originalPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);

        AuthEmailMessage verificationMessage = await _factory.Emails.WaitForAsync(email, TimeSpan.FromSeconds(5));
        string verificationToken = ExtractFragmentValue(verificationMessage.TextBody, "verify_token");
        using HttpResponseMessage verification = await client.PostAsJsonAsync("/auth/verify-email", new
        {
            token = verificationToken
        });
        Assert.Equal(HttpStatusCode.NoContent, verification.StatusCode);

        using HttpResponseMessage forgot = await client.PostAsJsonAsync("/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);

        AuthEmailMessage resetMessage = await _factory.Emails.WaitForAsync(email, TimeSpan.FromSeconds(5));
        string resetToken = ExtractFragmentValue(resetMessage.TextBody, "reset_token");
        using HttpResponseMessage reset = await client.PostAsJsonAsync("/auth/reset-password", new
        {
            token = resetToken,
            newPassword = replacementPassword
        });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using HttpResponseMessage oldLogin = await client.PostAsJsonAsync("/auth/login", new
        {
            email,
            password = originalPassword,
            tenantId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        TokenPayload replacementLogin = await PostTokenAsync(client, "/auth/login", new
        {
            email,
            password = replacementPassword,
            tenantId = (Guid?)null
        });
        Assert.False(string.IsNullOrWhiteSpace(replacementLogin.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(replacementLogin.RefreshToken));
    }

    [Fact]
    public async Task RevokeEndpointInvalidatesExplicitRefreshToken()
    {
        using HttpClient client = _factory.CreateClient();
        TokenPayload session = await PostTokenAsync(client, "/auth/login", new
        {
            email = "admin@test.local",
            password = "Admin123!Sample",
            tenantId = (Guid?)null
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using HttpResponseMessage revoke = await client.PostAsJsonAsync("/auth/revoke", new
        {
            refreshToken = session.RefreshToken,
            revokeFamily = false
        });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        using HttpResponseMessage refresh = await client.PostAsJsonAsync("/auth/refresh", new
        {
            refreshToken = session.RefreshToken,
            tenantId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private static async Task<TokenPayload> PostTokenAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<TokenPayload>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The token response was empty.");
    }

    private static string ExtractFragmentValue(string textBody, string name)
    {
        int uriStart = textBody.LastIndexOf("http", StringComparison.Ordinal);
        Assert.True(uriStart >= 0, "The email body did not contain an absolute link.");
        Uri uri = new(textBody[uriStart..]);
        string fragment = uri.Fragment.TrimStart('#');
        foreach (string pair in fragment.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"The fragment value '{name}' was not present.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record TokenPayload(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresUtc,
        string TokenType,
        string? RefreshToken,
        DateTimeOffset? RefreshTokenExpiresUtc);
}
