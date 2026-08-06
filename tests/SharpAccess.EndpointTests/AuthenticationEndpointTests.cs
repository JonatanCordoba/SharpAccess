using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharpAccess.EndpointTests;

public sealed class AuthenticationEndpointTests : IClassFixture<AuthenticationEndpointTests.AuthApplicationFactory>
{
    private readonly AuthApplicationFactory _factory;

    // Stores the shared application factory for endpoint tests.
    public AuthenticationEndpointTests(AuthApplicationFactory factory) => _factory = factory;

    // Verifies that static console and health endpoint are available.
    [Fact]
    public async Task StaticConsoleAndHealthEndpointAreAvailable()
    {
        using HttpClient client = _factory.CreateClient();
        string html = await client.GetStringAsync("/");
        Assert.Contains("SharpAccess", html, StringComparison.Ordinal);
        using HttpResponseMessage health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    // Verifies that malformed json returns sanitized problem details.
    [Fact]
    public async Task MalformedJsonReturnsSanitizedProblemDetails()
    {
        using HttpClient client = _factory.CreateClient();
        using StringContent content = new("{", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/auth/login", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("BadHttpRequestException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:", body, StringComparison.Ordinal);
    }

    // Verifies that anonymous administration request is challenged.
    [Fact]
    public async Task AnonymousAdministrationRequestIsChallenged()
    {
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // Verifies that login me admin and refresh rotation work.
    [Fact]
    public async Task LoginMeAdminAndRefreshRotationWork()
    {
        using HttpClient client = _factory.CreateClient();
        TokenPayload first = await LoginAsync(client);
        Assert.False(string.IsNullOrWhiteSpace(first.RefreshToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        using HttpResponseMessage me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using HttpResponseMessage users = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);

        TokenPayload rotated = await PostTokenAsync(client, "/auth/refresh", new
        {
            refreshToken = first.RefreshToken,
            tenantId = (Guid?)null
        });
        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);

        using HttpResponseMessage reuse = await client.PostAsJsonAsync("/auth/refresh", new
        {
            refreshToken = first.RefreshToken,
            tenantId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        using HttpResponseMessage replacement = await client.PostAsJsonAsync("/auth/refresh", new
        {
            refreshToken = rotated.RefreshToken,
            tenantId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Unauthorized, replacement.StatusCode);
    }

    // Verifies that cookie-backed refresh and logout accept an empty request body.
    [Fact]
    public async Task CookieBackedRefreshAndLogoutAcceptEmptyBodies()
    {
        using HttpClient client = _factory.CreateClient();
        TokenPayload first = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);

        using HttpRequestMessage refreshRequest = new(HttpMethod.Post, "/auth/refresh");
        using HttpResponseMessage refreshResponse = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        TokenPayload rotated = await ReadTokenAsync(refreshResponse);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);

        using HttpRequestMessage logoutRequest = new(HttpMethod.Post, "/auth/logout");
        using HttpResponseMessage logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
    }

    // Verifies that an authenticated user without administration permission receives 403.
    [Fact]
    public async Task AuthenticatedUserWithoutAdministrationPermissionIsForbidden()
    {
        using HttpClient client = _factory.CreateClient();
        string email = $"member-{Guid.NewGuid():N}@test.local";
        const string password = "Member123!Sample";
        using HttpResponseMessage registration = await client.PostAsJsonAsync("/auth/register", new { email, password });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);

        AuthEmailMessage message = await _factory.Emails.WaitForAsync(email, TimeSpan.FromSeconds(5));
        string token = ExtractFragmentValue(message.TextBody, "verify_token");
        using HttpResponseMessage verification = await client.PostAsJsonAsync("/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.NoContent, verification.StatusCode);

        TokenPayload session = await PostTokenAsync(client, "/auth/login", new
        {
            email,
            password,
            tenantId = (Guid?)null
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // Verifies that refresh cookie is http only and same site lax.
    [Fact]
    public async Task RefreshCookieIsHttpOnlyAndSameSiteLax()
    {
        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "admin@test.local",
            password = "Admin123!Sample",
            tenantId = (Guid?)null
        });
        response.EnsureSuccessStatusCode();
        string cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Path=/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    // Verifies that login asynchronously.
    private static Task<TokenPayload> LoginAsync(HttpClient client) => PostTokenAsync(client, "/auth/login", new
    {
        email = "admin@test.local",
        password = "Admin123!Sample",
        tenantId = (Guid?)null
    });

    // Posts one token request and parses the successful response.
    private static async Task<TokenPayload> PostTokenAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await ReadTokenAsync(response);
    }

    // Parses one token response without consuming caller-owned response disposal.
    private static async Task<TokenPayload> ReadTokenAsync(HttpResponseMessage response)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        TokenPayload? payload = await JsonSerializer.DeserializeAsync<TokenPayload>(stream, JsonOptions);
        return payload ?? throw new InvalidOperationException("The token response was empty.");
    }

    // Extracts and decodes one value from a URI fragment contained in an email body.
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

    public sealed class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentDictionary<string, AuthEmailMessage> _messages =
            new(StringComparer.OrdinalIgnoreCase);

        // Stores the newest message for each recipient.
        public Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            cancellationToken.ThrowIfCancellationRequested();
            _messages[message.Recipient] = message;
            return Task.CompletedTask;
        }

        // Waits for a captured message without making the production flow synchronous.
        public async Task<AuthEmailMessage> WaitForAsync(
            string recipient,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource timeoutSource = new(timeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            while (!linked.IsCancellationRequested)
            {
                if (_messages.TryGetValue(recipient, out AuthEmailMessage? message))
                {
                    return message;
                }

                await Task.Delay(10, linked.Token);
            }

            throw new TimeoutException($"No email was captured for '{recipient}'.");
        }
    }

    public sealed class AuthApplicationFactory : WebApplicationFactory<Program>, IDisposable
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"sharpaccess-http-{Guid.NewGuid():N}.db");

        public CapturingEmailSender Emails { get; } = new();

        // Configures an isolated test host and database.
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(ResolveSampleContentRoot());
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Auth"] = $"Data Source={_databasePath};Pooling=False",
                    ["APP_PORT"] = "5000",
                    ["Auth:BaseUri"] = "http://localhost:5000",
                    ["Auth:SigningKey"] = "TEST-ONLY-JWT-SIGNING-KEY-12345678901234567890",
                    ["Auth:TokenHashingKey"] = "TEST-ONLY-TOKEN-HASHING-KEY-12345678901234567890",
                    ["Auth:PasswordPepper"] = "TEST-ONLY-PASSWORD-PEPPER-12345678901234567890",
                    ["Auth:ReturnRefreshTokenInResponseBody"] = "true",
                    ["Auth:SeedTestAdmin"] = "true",
                    ["Auth:TestAdminEmail"] = "admin@test.local",
                    ["Auth:TestAdminPassword"] = "Admin123!Sample"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
            });
        }

        // Locates the physical sample content root used by WebApplicationFactory.
        private static string ResolveSampleContentRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo? directory = new(start);
                while (directory is not null)
                {
                    string candidate = Path.Combine(directory.FullName, "samples", "SharpAccess.SampleApi");
                    if (File.Exists(Path.Combine(candidate, "appsettings.json"))
                        && Directory.Exists(Path.Combine(candidate, "wwwroot")))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            throw new InvalidOperationException("Could not locate the sample API content root.");
        }

        // Deletes the isolated database when the factory is disposed.
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }
}
