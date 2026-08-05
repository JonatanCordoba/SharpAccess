using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.OAuth;
using SharpAccess.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.IntegrationTests;

[Trait("Category", "OidcDeterministic")]
public sealed class OidcEmulatorIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string ProviderName = "emulator";
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private DeterministicOidcHandler _emulator = null!;
    private HttpClient _httpClient = null!;
    private int _disposed;

    // Creates a complete package service graph backed by SQLite and the deterministic OIDC transport emulator.
    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"sharpaccess-oidc-{Guid.NewGuid():N}.db");
        _emulator = new DeterministicOidcHandler();
        _httpClient = new HttpClient(_emulator, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSharpAccess(Configure);
        services.AddSqliteAccess(options => options.ConnectionString = $"Data Source={_databasePath};Pooling=False");
        services.RemoveAll<IExternalOAuthProvider>();
        services.AddScoped<IExternalOAuthProvider>(serviceProvider =>
            new OpenIdConnectOAuthProvider(
                new StaticHttpClientFactory(_httpClient),
                serviceProvider.GetRequiredService<IMemoryCache>(),
                serviceProvider.GetRequiredService<IAuthClock>(),
                serviceProvider.GetRequiredService<IOptions<AuthOptions>>()));

        _provider = services.BuildServiceProvider(validateScopes: true);
        await _provider.InitializeSharpAccessAsync().ConfigureAwait(false);
    }

    // Disposes the provider asynchronously, then releases emulator and SQLite resources exactly once.
    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_provider is not null)
            {
                await _provider.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            DisposeOwnedResources();
        }
    }

    // Supports analyzers and runners that use the synchronous xUnit disposal path.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _provider?.Dispose();
        }
        finally
        {
            DisposeOwnedResources();
            GC.SuppressFinalize(this);
        }
    }

    // Proves a non-Google keyed provider completes challenge, PKCE exchange, signed identity validation, linking, and local session issuance.
    [Fact]
    public async Task GenericProviderCompletesAuthorizationCodePkceFlowAndConsumesArtifactsOnce()
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IOAuthService service = scope.ServiceProvider.GetRequiredService<IOAuthService>();
        RequestMetadata metadata = new("127.0.0.1", "oidc-emulator-integration");

        ServiceResult<Uri> challenge = await service.CreateChallengeAsync(
            ProviderName,
            "/signed-in",
            metadata);
        Assert.True(challenge.Succeeded);
        Uri authorizationUri = Assert.IsType<Uri>(challenge.Value);
        var authorizationQuery = QueryHelpers.ParseQuery(authorizationUri.Query);
        string state = authorizationQuery["state"].ToString();
        string nonce = authorizationQuery["nonce"].ToString();
        string codeChallenge = authorizationQuery["code_challenge"].ToString();

        Assert.Equal("code", authorizationQuery["response_type"]);
        Assert.Equal("S256", authorizationQuery["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(state));
        Assert.False(string.IsNullOrWhiteSpace(nonce));
        Assert.False(string.IsNullOrWhiteSpace(codeChallenge));
        _emulator.Expect(nonce, codeChallenge);

        ServiceResult<Uri> callback = await service.HandleCallbackAsync(
            ProviderName,
            DeterministicOidcHandler.AuthorizationCode,
            state,
            null,
            metadata);
        Assert.True(callback.Succeeded);
        Uri returnUri = Assert.IsType<Uri>(callback.Value);
        Assert.Equal("https://app.test/signed-in", returnUri.GetLeftPart(UriPartial.Path));
        string exchangeCode = QueryHelpers.ParseQuery("?" + returnUri.Fragment.TrimStart('#'))["oauth_code"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(exchangeCode));

        ServiceResult<SessionTokens> session = await service.ExchangeAsync(
            ProviderName,
            exchangeCode,
            null,
            metadata);
        Assert.True(session.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(session.Value?.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(session.Value?.RefreshToken));
        Assert.True(_emulator.ProtocolValidated);
        Assert.Equal(1, _emulator.TokenRequestCount);
        Assert.Equal(1, _emulator.JsonWebKeySetRequestCount);

        Assert.False((await service.HandleCallbackAsync(
            ProviderName,
            DeterministicOidcHandler.AuthorizationCode,
            state,
            null,
            metadata)).Succeeded);
        Assert.False((await service.ExchangeAsync(
            ProviderName,
            exchangeCode,
            null,
            metadata)).Succeeded);
        Assert.Equal(1, _emulator.TokenRequestCount);
    }

    // Configures only a generic emulator entry so Google cannot be required by orchestration or persistence.
    private static void Configure(AuthOptions options)
    {
        options.BaseUri = new Uri("https://app.test");
        options.JwtIssuer = "oidc-emulator-tests";
        options.JwtAudience = "oidc-emulator-clients";
        options.JwtSigningKey = "OIDC-EMULATOR-JWT-SIGNING-KEY-12345678901234567890";
        options.Features.RefreshTokens = true;
        options.TokenHashing.Key = "OIDC-EMULATOR-TOKEN-HASHING-KEY-123456789012345678";
        options.RateLimits.PartitionKey = "OIDC-EMULATOR-RATE-LIMIT-KEY-12345678901234567890";
        options.RefreshCookieSecurePolicy = CookieSecurePolicy.Always;
        options.RefreshTokenCookieName = "__Secure-sharpaccess_refresh";
        options.RequireCsrfHeaderForCookieRefreshRequests = true;
        options.Migrations.Mode = SharpAccessMigrationMode.ApplyAtStartup;
        options.OpenIdConnect.Providers.Clear();
        options.OpenIdConnect.Providers[ProviderName] = new OpenIdConnectProviderOptions
        {
            Enabled = true,
            ClientId = DeterministicOidcHandler.ClientId,
            ClientSecret = DeterministicOidcHandler.ClientSecret,
            ClientAuthenticationMethod = OpenIdConnectClientAuthenticationMethod.ClientSecretPost,
            CallbackPath = "/auth/oauth/emulator/callback",
            AuthorizationEndpoint = new Uri("https://oidc.test/authorize"),
            TokenEndpoint = new Uri("https://oidc.test/token"),
            JsonWebKeySetEndpoint = new Uri("https://oidc.test/jwks"),
            ValidIssuers = [DeterministicOidcHandler.Issuer],
            Scopes = ["openid", "email", "profile"],
            SigningAlgorithms = [SecurityAlgorithms.RsaSha256],
            AllowedHosts = ["oidc.test"]
        };
    }

    // Releases disposable test resources and removes temporary SQLite sidecars.
    private void DisposeOwnedResources()
    {
        _httpClient?.Dispose();
        _emulator?.Dispose();
        SqliteConnection.ClearAllPools();
        DeleteIfPresent(_databasePath);
        DeleteIfPresent(_databasePath + "-wal");
        DeleteIfPresent(_databasePath + "-shm");
    }

    // Removes one best-effort temporary artifact.
    private static void DeleteIfPresent(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        // Returns the isolated emulator client without creating a network-capable fallback.
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(OpenIdConnectOAuthProvider.HttpClientName, name);
            return client;
        }
    }

    private sealed class DeterministicOidcHandler : HttpMessageHandler
    {
        internal const string AuthorizationCode = "emulator-authorization-code";
        internal const string ClientId = "emulator-client";
        internal const string ClientSecret = "emulator-client-secret-value";
        internal const string Issuer = "https://oidc.test";
        private const string KeyId = "emulator-signing-key";
        private readonly RSA _signingKey = RSA.Create(2_048);
        private string? _expectedNonce;
        private string? _expectedCodeChallenge;

        internal bool ProtocolValidated { get; private set; }
        internal int TokenRequestCount { get; private set; }
        internal int JsonWebKeySetRequestCount { get; private set; }

        // Captures challenge material needed to validate the later token request without exposing the PKCE verifier.
        internal void Expect(string nonce, string codeChallenge)
        {
            _expectedNonce = nonce;
            _expectedCodeChallenge = codeChallenge;
        }

        // Serves bounded token and JWKS responses entirely in process.
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri?.AbsolutePath == "/token")
            {
                TokenRequestCount++;
                string body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var form = QueryHelpers.ParseQuery("?" + body);
                string verifier = form["code_verifier"].ToString();
                string actualChallenge = Base64UrlEncoder.Encode(
                    SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
                ProtocolValidated = request.Method == HttpMethod.Post
                    && string.Equals(form["code"].ToString(), AuthorizationCode, StringComparison.Ordinal)
                    && string.Equals(form["grant_type"].ToString(), "authorization_code", StringComparison.Ordinal)
                    && string.Equals(form["redirect_uri"].ToString(), "https://app.test/auth/oauth/emulator/callback", StringComparison.Ordinal)
                    && string.Equals(form["client_id"].ToString(), ClientId, StringComparison.Ordinal)
                    && string.Equals(form["client_secret"].ToString(), ClientSecret, StringComparison.Ordinal)
                    && string.Equals(actualChallenge, _expectedCodeChallenge, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(_expectedNonce);
                return ProtocolValidated
                    ? JsonResponse(request, HttpStatusCode.OK, JsonSerializer.Serialize(new { id_token = CreateIdentityToken() }))
                    : JsonResponse(request, HttpStatusCode.BadRequest, "{}");
            }

            if (request.RequestUri?.AbsolutePath == "/jwks")
            {
                JsonWebKeySetRequestCount++;
                return JsonResponse(request, HttpStatusCode.OK, CreateJsonWebKeySet());
            }

            return JsonResponse(request, HttpStatusCode.NotFound, "{}");
        }

        // Creates a signed identity token with the exact issuer, audience, nonce, and bounded identity claims expected by the provider.
        private string CreateIdentityToken()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RsaSecurityKey key = new(_signingKey) { KeyId = KeyId };
            SecurityTokenDescriptor descriptor = new()
            {
                Issuer = Issuer,
                Audience = ClientId,
                IssuedAt = now.UtcDateTime,
                NotBefore = now.AddSeconds(-5).UtcDateTime,
                Expires = now.AddMinutes(5).UtcDateTime,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
                Claims = new Dictionary<string, object>
                {
                    [JwtRegisteredClaimNames.Sub] = "emulator-subject",
                    [JwtRegisteredClaimNames.Email] = "emulated.user@example.com",
                    ["email_verified"] = "true",
                    ["nonce"] = _expectedNonce!,
                    ["name"] = "Emulated User"
                }
            };
            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        // Serializes the emulator's public RSA key as a minimal JWKS document.
        private string CreateJsonWebKeySet()
        {
            RSAParameters parameters = _signingKey.ExportParameters(includePrivateParameters: false);
            return JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = KeyId,
                        alg = SecurityAlgorithms.RsaSha256,
                        n = Base64UrlEncoder.Encode(parameters.Modulus!),
                        e = Base64UrlEncoder.Encode(parameters.Exponent!)
                    }
                }
            });
        }

        // Creates one JSON response whose final URI remains available to the provider host allowlist check.
        private static HttpResponseMessage JsonResponse(
            HttpRequestMessage request,
            HttpStatusCode statusCode,
            string json) => new(statusCode)
        {
            RequestMessage = request,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // Releases the ephemeral signing key.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _signingKey.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
