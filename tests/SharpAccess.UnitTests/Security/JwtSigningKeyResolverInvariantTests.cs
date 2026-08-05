using SharpAccess.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests;

public sealed class JwtSigningKeyResolverInvariantTests
{
    [Trait("MutationInvariant", "JwtKeySelection")]
    [Fact]
    public void JwtSigningKeyResolverRejectsMissingAndUnknownKeyIdentifiers()
    {
        AuthOptions options = TestOptions.Create();
        JwtBearerOptions bearer = new();
        Microsoft.Extensions.DependencyInjection.AuthJwtBearerConfiguration.ConfigureJwtBearer(
            bearer,
            Options.Create(options),
            TestOptions.Clock);
        IssuerSigningKeyResolver resolver = bearer.TokenValidationParameters.IssuerSigningKeyResolver!;

        Assert.Empty(resolver(string.Empty, null!, null, bearer.TokenValidationParameters));
        Assert.Empty(resolver(string.Empty, null!, "unknown-key", bearer.TokenValidationParameters));
    }
}
