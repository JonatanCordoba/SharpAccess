using SharpAccess;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests;

public sealed class AuthClockDeterminismTests
{
    private static readonly DateTimeOffset Now =
        new(2035, 4, 5, 6, 7, 8, TimeSpan.Zero);

    // Verifies option and key-ring windows use the same injected clock.
    [Fact]
    public void OptionsValidationAndConfiguredKeyRingUseTheInjectedClock()
    {
        AuthOptions options = RotatingSigningOptions();
        MutableClock clock = new(Now);

        ValidateOptionsResult active = new AuthOptionsValidator(clock).Validate(null, options);
        using ConfiguredAccessTokenSigningKeyRing ring =
            new(Options.Create(options), clock);

        Assert.True(active.Succeeded, string.Join(Environment.NewLine, active.Failures ?? []));
        Assert.Equal("current", ring.ActiveSigningKey.KeyId);

        clock.UtcNow = Now.AddMinutes(1);
        ValidateOptionsResult retired = new AuthOptionsValidator(clock).Validate(null, options);

        Assert.False(retired.Succeeded);
        Assert.Contains(
            retired.Failures!,
            static failure => failure.Contains("retired key", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(
            () => new ConfiguredAccessTokenSigningKeyRing(Options.Create(options), clock));
    }

    // Verifies JWT signing-key resolution observes the injected validation clock.
    [Fact]
    public void JwtKeyResolutionUsesTheInjectedClockAtValidationTime()
    {
        AuthOptions options = RotatingSigningOptions();
        MutableClock clock = new(Now);
        using ConfiguredAccessTokenSigningKeyRing ring =
            new(Options.Create(options), clock);
        JwtBearerOptions bearer = new();
        Microsoft.Extensions.DependencyInjection.AuthJwtBearerConfiguration.ConfigureJwtBearer(
            bearer,
            Options.Create(options),
            ring,
            clock);
        IssuerSigningKeyResolver resolver =
            bearer.TokenValidationParameters.IssuerSigningKeyResolver!;

        Assert.Single(resolver(string.Empty, null!, "current", bearer.TokenValidationParameters));

        clock.UtcNow = Now.AddMinutes(1);

        Assert.Empty(resolver(string.Empty, null!, "current", bearer.TokenValidationParameters));
    }

    // Verifies startup validation rejects an active key until its configured not-before instant.
    [Fact]
    public void OptionsValidationRejectsAnActiveKeyWithAFutureNotBeforeTime()
    {
        AuthOptions options = RotatingSigningOptions();
        options.AccessTokenSigning.HmacSha256Keys["current"].NotBeforeUtc = Now.AddSeconds(1);
        MutableClock clock = new(Now);

        ValidateOptionsResult premature = new AuthOptionsValidator(clock).Validate(null, options);

        Assert.False(premature.Succeeded);
        Assert.Contains(
            premature.Failures!,
            static failure => failure.Contains("not-before time is in the future", StringComparison.Ordinal));

        clock.UtcNow = Now.AddSeconds(1);
        ValidateOptionsResult active = new AuthOptionsValidator(clock).Validate(null, options);

        Assert.True(active.Succeeded, string.Join(Environment.NewLine, active.Failures ?? []));
    }

    // Verifies startup validation rejects an active key until its configured activation instant.
    [Fact]
    public void OptionsValidationRejectsAnActiveKeyWithAFutureActivationTime()
    {
        AuthOptions options = RotatingSigningOptions();
        options.AccessTokenSigning.HmacSha256Keys["current"].ActivatedUtc = Now.AddSeconds(1);
        MutableClock clock = new(Now);

        ValidateOptionsResult premature = new AuthOptionsValidator(clock).Validate(null, options);

        Assert.False(premature.Succeeded);
        Assert.Contains(
            premature.Failures!,
            static failure => failure.Contains("activation time is in the future", StringComparison.Ordinal));

        clock.UtcNow = Now.AddSeconds(1);
        ValidateOptionsResult active = new AuthOptionsValidator(clock).Validate(null, options);

        Assert.True(active.Succeeded, string.Join(Environment.NewLine, active.Failures ?? []));
    }

    // Creates a signing key whose validity boundary is controlled by the test clock.
    private static AuthOptions RotatingSigningOptions()
    {
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = string.Empty;
        options.AccessTokenSigning.ActiveKeyId = "current";
        options.AccessTokenSigning.HmacSha256Keys["current"] = new HmacAccessTokenSigningKeyOptions
        {
            Key = Convert.ToBase64String(
                Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray()),
            ActivatedUtc = Now.AddMinutes(-1),
            RetiredUtc = Now.AddMinutes(1)
        };
        return options;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IAuthClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
