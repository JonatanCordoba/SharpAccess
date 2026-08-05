using SharpAccess.Configuration;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class OpenIdConnectConfigurationTests
{
    // Verifies one fully specified keyed provider passes startup validation.
    [Fact]
    public void KeyedGenericProviderConfigurationPassesValidation()
    {
        AuthOptions options = TestOptions.Create();
        options.OpenIdConnect.Providers["contoso"] = Provider(
            callbackPath: "/auth/oauth/contoso/callback");

        ValidateOptionsResult result =
            new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures ?? []));
    }

    // Verifies the Google-compatible defaults remain configuration data when assigned to another provider key.
    [Fact]
    public void GoogleCompatibleDefaultsDoNotDependOnTheProviderKey()
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions renamed = OpenIdConnectProviderOptions.CreateGoogleDefaults();
        renamed.Enabled = true;
        renamed.ClientId = "workforce-client";
        renamed.ClientSecret = "workforce-client-secret";
        renamed.CallbackPath = "/auth/oauth/workforce/callback";
        options.OpenIdConnect.Providers.Remove("google");
        options.OpenIdConnect.Providers["workforce"] = renamed;

        ValidateOptionsResult result =
            new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures ?? []));
    }

    // Verifies unsafe provider trust boundaries fail closed with a specific diagnostic.
    [Theory]
    [MemberData(nameof(InvalidProviderConfigurations))]
    public void ProviderContractRejectsUnsafeOrAmbiguousConfiguration(
        Action<AuthOptions> arrange,
        string expectedFailure)
    {
        AuthOptions options = TestOptions.Create();
        arrange(options);

        ValidateOptionsResult result =
            new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    // Verifies two enabled providers cannot own the same callback route.
    [Fact]
    public void MultipleEnabledProvidersRequireUniqueCallbackPaths()
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "google-client";
        google.ClientSecret = "google-client-secret";
        options.OpenIdConnect.Providers["contoso"] =
            Provider(callbackPath: google.CallbackPath);

        ValidateOptionsResult result =
            new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static failure => failure.Contains("CallbackPath must be unique", StringComparison.Ordinal));
    }

    // Verifies callback uniqueness matches ASP.NET Core's case-insensitive route matching.
    [Fact]
    public void CallbackPathsAreComparedCaseInsensitivelyLikeAspNetRouting()
    {
        AuthOptions options = TestOptions.Create();
        options.OpenIdConnect.Providers["contoso"] =
            Provider(callbackPath: "/auth/oauth/contoso/callback");
        OpenIdConnectProviderOptions fabrikam =
            Provider(callbackPath: "/AUTH/OAUTH/CONTOSO/CALLBACK");
        fabrikam.ClientId = "fabrikam-client";
        fabrikam.ClientSecret = "fabrikam-client-secret";
        options.OpenIdConnect.Providers["fabrikam"] = fabrikam;

        ValidateOptionsResult result =
            new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static failure => failure.Contains("CallbackPath must be unique", StringComparison.Ordinal));
    }

    // Verifies callback paths cannot contain route syntax or ambiguous encoded segments.
    [Theory]
    [InlineData("/auth/oauth/{provider}/callback")]
    [InlineData("/auth/oauth/{*provider}/callback")]
    [InlineData("/auth/oauth/[provider]/callback")]
    [InlineData("/auth/oauth/*/callback")]
    [InlineData("/auth/oauth/contoso callback")]
    [InlineData("/auth/oauth/contoso%2Fcallback")]
    [InlineData("/auth/oauth/./callback")]
    [InlineData("/auth/oauth/../callback")]
    public void CallbackPathsRejectAmbiguousOrNonLiteralSyntax(string callbackPath)
    {
        AuthOptions options = TestOptions.Create();
        options.OpenIdConnect.Providers["contoso"] = Provider(callbackPath);

        ValidateOptionsResult result =
            new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static failure => failure.Contains("exact literal path", StringComparison.Ordinal));
    }

    // Verifies callback paths cannot shadow any literal or parameterized SharpAccess route.
    [Theory]
    [MemberData(nameof(ReservedSharpAccessRoutes))]
    public void CallbackPathsCannotCollideWithReservedSharpAccessRoutes(string callbackPath)
    {
        AuthOptions options = TestOptions.Create();
        options.OpenIdConnect.Providers["contoso"] = Provider(callbackPath);

        ValidateOptionsResult result =
            new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static failure => failure.Contains("route reserved by SharpAccess", StringComparison.Ordinal));
    }

    // Verifies trailing slashes cannot bypass route-equivalent callback uniqueness.
    [Fact]
    public void CallbackPathUniquenessNormalizesTrailingSlashes()
    {
        AuthOptions options = TestOptions.Create();
        options.OpenIdConnect.Providers["contoso"] =
            Provider(callbackPath: "/callbacks/contoso");
        OpenIdConnectProviderOptions fabrikam =
            Provider(callbackPath: "/CALLBACKS/CONTOSO/");
        fabrikam.ClientId = "fabrikam-client";
        fabrikam.ClientSecret = "fabrikam-client-secret";
        options.OpenIdConnect.Providers["fabrikam"] = fabrikam;

        ValidateOptionsResult result =
            new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static failure => failure.Contains("CallbackPath must be unique", StringComparison.Ordinal));
    }

    // Supplies one concrete collision for every reserved SharpAccess route pattern.
    public static TheoryData<string> ReservedSharpAccessRoutes => new()
    {
        "/auth/register",
        "/auth/verify-email",
        "/auth/resend-verification",
        "/auth/login",
        "/auth/change-password",
        "/auth/forgot-password",
        "/auth/reset-password",
        "/auth/refresh",
        "/auth/logout",
        "/auth/revoke",
        "/auth/me",
        "/auth/oauth/contoso/challenge",
        "/auth/oauth/contoso/exchange",
        "/admin/users",
        "/admin/users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/status",
        "/admin/roles",
        "/admin/roles/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "/admin/permissions",
        "/admin/roles/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/permissions",
        "/admin/roles/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/permissions/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        "/admin/users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/roles",
        "/admin/users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/roles/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        "/admin/audit-logs",
        "/tenants",
        "/tenants/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "/tenants/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/owner",
        "/tenants/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/owner/transfer",
        "/tenants/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/members",
        "/tenants/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/members/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/roles"
    };

    public static TheoryData<Action<AuthOptions>, string> InvalidProviderConfigurations => new()
    {
        {
            options => options.OpenIdConnect.Providers["Contoso"] = Provider(),
            "lowercase provider name"
        },
        {
            options =>
            {
                OpenIdConnectProviderOptions provider = Provider();
                provider.AllowedHosts = [];
                options.OpenIdConnect.Providers["contoso"] = provider;
            },
            "AllowedHosts"
        },
        {
            options =>
            {
                OpenIdConnectProviderOptions provider = Provider();
                provider.TokenEndpoint = new Uri("https://outside.example/token");
                options.OpenIdConnect.Providers["contoso"] = provider;
            },
            "TokenEndpoint host"
        },
        {
            options =>
            {
                OpenIdConnectProviderOptions provider = Provider();
                provider.SigningAlgorithms = ["none"];
                options.OpenIdConnect.Providers["contoso"] = provider;
            },
            "SigningAlgorithms"
        },
        {
            options =>
            {
                OpenIdConnectProviderOptions provider = Provider();
                provider.Scopes = ["email"];
                options.OpenIdConnect.Providers["contoso"] = provider;
            },
            "Scopes must contain openid"
        },
        {
            options =>
            {
                OpenIdConnectProviderOptions provider = Provider();
                provider.ValidIssuers = ["http://issuer.example"]; // DevSkim: ignore DS137138 -- Intentional rejection fixture.
                options.OpenIdConnect.Providers["contoso"] = provider;
            },
            "HTTPS issuer"
        },
        {
            options =>
            {
                OpenIdConnectProviderOptions provider = Provider();
                provider.Prompt = null!;
                options.OpenIdConnect.Providers["contoso"] = provider;
            },
            "Prompt"
        },
        {
            options => options.OpenIdConnect.Providers["contoso"] = null!,
            "cannot be null"
        },
        {
            options =>
            {
                OpenIdConnectProviderOptions provider = Provider();
                provider.ClientAuthenticationMethod = (OpenIdConnectClientAuthenticationMethod)999;
                options.OpenIdConnect.Providers["contoso"] = provider;
            },
            "ClientAuthenticationMethod"
        }
    };

    // Creates a complete generic provider configuration for focused mutations.
    private static OpenIdConnectProviderOptions Provider(
        string callbackPath = "/auth/oauth/contoso/callback") => new()
    {
        Enabled = true,
        ClientId = "contoso-client",
        ClientSecret = "contoso-client-secret",
        CallbackPath = callbackPath,
        AuthorizationEndpoint = new Uri("https://login.contoso.example/authorize"),
        TokenEndpoint = new Uri("https://login.contoso.example/token"),
        JsonWebKeySetEndpoint = new Uri("https://login.contoso.example/jwks"),
        ValidIssuers = ["https://login.contoso.example"],
        Scopes = ["openid", "email", "profile"],
        SigningAlgorithms = ["RS256"],
        AllowedHosts = ["login.contoso.example"]
    };
}
