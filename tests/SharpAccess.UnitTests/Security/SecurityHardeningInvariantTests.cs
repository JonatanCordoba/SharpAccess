using System.Net;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using SharpAccess;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpAccess.Security;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests;

public sealed class SecurityHardeningInvariantTests
{
    [Fact]
    public void AccessTokenKeyMetadataRejectsUnsupportedValidityWindows()
    {
        byte[] material = RandomNumberGenerator.GetBytes(32);
        SymmetricSecurityKey key = new(material);
        try
        {
            Assert.Throws<ArgumentException>(() => new AccessTokenVerificationKey(
                "key-1",
                key,
                SecurityAlgorithms.HmacSha256,
                TestOptions.Now,
                TestOptions.Now.AddMinutes(2),
                TestOptions.Now.AddMinutes(1)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    [Fact]
    public void ConfiguredHmacRingRetainsHistoricalVerificationKeys()
    {
        ServiceCollection services = new();
        services.AddSingleton<IAuthClock>(TestOptions.Clock);
        services.AddSharpAccess(options =>
        {
            options.AccessTokenSigning.ActiveKeyId = "current";
            options.AccessTokenSigning.HmacSha256Keys["current"] = new HmacAccessTokenSigningKeyOptions
            {
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ActivatedUtc = TestOptions.Now.AddMinutes(-1)
            };
            options.AccessTokenSigning.HmacSha256Keys["previous"] = new HmacAccessTokenSigningKeyOptions
            {
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ActivatedUtc = TestOptions.Now.AddDays(-1),
                RetiredUtc = TestOptions.Now.AddMinutes(10)
            };
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        IAccessTokenSigningKeyRing ring = provider.GetRequiredService<IAccessTokenSigningKeyRing>();
        Assert.Equal("current", ring.ActiveSigningKey.KeyId);
        Assert.Equal(2, ring.VerificationKeys.Count);
    }

    [Fact]
    public void ProductionRejectsMigrationOnlyAndPredictableSecrets()
    {
        ServiceCollection services = new();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(Environments.Production));
        services.AddSharpAccess(options =>
        {
            options.BaseUri = new Uri("https://app.example");
            options.Features.PasswordAuthentication = true;
            options.JwtSigningKey = "example-example-example-example-example";
            options.Passwords.Peppers["v1"] = "example-example-example-example";
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<AuthOptions>>().Value);
        Assert.Contains(exception.Failures, failure => failure.Contains("predictable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(exception.Failures, failure => failure.Contains("migration-only", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void VersionedTokenHashingKeepsAcceptedHistoricalCandidates()
    {
        ServiceCollection services = new();
        services.AddSharpAccess(options =>
        {
            options.TokenHashing.CurrentKeyVersion = "v2";
            options.TokenHashing.Keys["v2"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            options.TokenHashing.Keys["v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        Type protectorType = typeof(AuthOptions).Assembly.GetType(
            "SharpAccess.Security.ITokenProtector",
            throwOnError: true)!;
        object protector = provider.GetRequiredService(protectorType);
        MethodInfo method = protectorType.GetMethod("HashCandidates")!;
        IEnumerable<string> candidates = (IEnumerable<string>)method.Invoke(protector, ["opaque-token"])!;
        string[] values = candidates.ToArray();
        Assert.Equal(3, values.Length);
        Assert.All(values, value => Assert.Equal(64, value.Length));
        Assert.NotEqual(values[0], values[1]);
    }

    [Fact]
    public void HostProvidedRsaSigningCredentialsAreRepresentable()
    {
        using RSA rsa = RSA.Create(2048);
        RsaSecurityKey key = new(rsa) { KeyId = "rsa-2026" };
        SigningCredentials credentials = new(key, SecurityAlgorithms.RsaSha256);
        AccessTokenSigningKey signingKey = new(
            "rsa-2026",
            credentials,
            key,
            TestOptions.Now.AddMinutes(-1));
        Assert.Equal(SecurityAlgorithms.RsaSha256, signingKey.Algorithm);
        Assert.Same(credentials, signingKey.SigningCredentials);
    }


    [Fact]
    public void AlgorithmConfusionIsRejectedBeforeTokenValidation()
    {
        byte[] material = RandomNumberGenerator.GetBytes(32);
        try
        {
            AccessTokenVerificationKey confused = new(
                "confused",
                new SymmetricSecurityKey(material),
                SecurityAlgorithms.RsaSha256,
                TestOptions.Now.AddMinutes(-1));
            Assert.Throws<InvalidOperationException>(() =>
                AccessTokenKeyRingGuard.ValidateKey(
                    confused,
                    requirePrivateSigningMaterial: false,
                    now: TestOptions.Now));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    [Fact]
    public void RateLimitPartitionKeysDoNotExposeAccountOrIpIdentifiers()
    {
        AuthOptions options = TestOptions.Create();
        options.RateLimits.PartitionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using AuthRateLimitPartitionKeyProvider provider = new(Options.Create(options));
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.25");

        string key = provider.CreatePartitionKey(context, "login", "person@example.com");

        Assert.DoesNotContain("person@example.com", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.0.2.25", key, StringComparison.Ordinal);
        Assert.StartsWith("login|", key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KAnonymityValidatorSendsOnlyRangePrefixAndRejectsAListedSuffix()
    {
        CapturingHandler handler = new("1E4C9B93F3F0682250B6CF8331B7EE68FD8:42\n");
        AuthOptions options = TestOptions.Create();
        options.Passwords.BreachedPasswords.Enabled = true;
        using BreachedPasswordRiskValidator validator = new(
            new FakeHttpClientFactory(handler),
            Options.Create(options));

        bool allowed = await validator.IsAllowedAsync("password", null);

        Assert.False(allowed);
        Assert.NotNull(handler.RequestUri);
        Assert.EndsWith("/5BAA6", handler.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain("password", handler.RequestUri.PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordHashQueueRejectsWorkBeyondItsBoundedCapacity()
    {
        PasswordSecurityOptions options = new()
        {
            MaximumConcurrentPasswordHashes = 1,
            MaximumQueuedPasswordHashes = 1,
            PasswordHashQueueTimeout = TimeSpan.FromSeconds(5)
        };
        PasswordHashConcurrencyLimiter limiter = PasswordHashConcurrencyLimiter.Get(options);
        await using PasswordHashConcurrencyLimiter.Lease first = await limiter.AcquireAsync(CancellationToken.None);
        ValueTask<PasswordHashConcurrencyLimiter.Lease> queued = limiter.AcquireAsync(CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => limiter.QueuedCount == 1, TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await limiter.AcquireAsync(CancellationToken.None));
        await first.DisposeAsync();
        await using PasswordHashConcurrencyLimiter.Lease second = await queued;
    }

    // Verifies that a zero-length password-hash queue admits free capacity and rejects contention.
    [Fact]
    public async Task ZeroLengthPasswordHashQueueAdmitsFreeCapacityAndRejectsOnlyContention()
    {
        PasswordSecurityOptions options = new()
        {
            MaximumConcurrentPasswordHashes = 1,
            MaximumQueuedPasswordHashes = 0,
            PasswordHashQueueTimeout = TimeSpan.FromMilliseconds(3_217)
        };
        PasswordHashConcurrencyLimiter limiter = PasswordHashConcurrencyLimiter.Get(options);

        await using PasswordHashConcurrencyLimiter.Lease first =
            await limiter.AcquireAsync(CancellationToken.None);
        Assert.Equal(0, limiter.QueuedCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await limiter.AcquireAsync(CancellationToken.None));
        Assert.Equal(0, limiter.QueuedCount);

        await first.DisposeAsync();
        await using PasswordHashConcurrencyLimiter.Lease afterRelease =
            await limiter.AcquireAsync(CancellationToken.None);
        Assert.Equal(0, limiter.QueuedCount);
    }

    private sealed class FakeEnvironment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "SharpAccess.UnitTests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            _ = name;
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.ASCII, "text/plain")
            };
            return Task.FromResult(response);
        }
    }

}
