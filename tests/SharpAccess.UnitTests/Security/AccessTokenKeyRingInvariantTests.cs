using System.Security.Cryptography;
using SharpAccess;
using SharpAccess.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests;

public sealed class AccessTokenKeyRingInvariantTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VerificationKeyMetadataValidatesIdentifiersAlgorithmsAndWindows()
    {
        Assert.Throws<ArgumentException>(() => new AccessTokenVerificationKey(
            new string('k', 129),
            Symmetric(),
            SecurityAlgorithms.HmacSha256,
            Now));

        Assert.Throws<ArgumentException>(() => new AccessTokenVerificationKey(
            "control\u0001",
            Symmetric(),
            SecurityAlgorithms.HmacSha256,
            Now));

        Assert.Throws<ArgumentException>(() => new AccessTokenVerificationKey(
            "key",
            Symmetric(),
            " ",
            Now));

        Assert.Throws<ArgumentException>(() => new AccessTokenVerificationKey(
            "key",
            Symmetric(),
            SecurityAlgorithms.HmacSha256,
            Now,
            Now.AddMinutes(2),
            Now.AddMinutes(1)));

        SymmetricSecurityKey mismatched = Symmetric();
        mismatched.KeyId = "other";
        Assert.Throws<ArgumentException>(() => new AccessTokenVerificationKey(
            "key",
            mismatched,
            SecurityAlgorithms.HmacSha256,
            Now));

        SymmetricSecurityKey matching = Symmetric();
        matching.KeyId = "key";
        AccessTokenVerificationKey accepted = new(
            "key",
            matching,
            SecurityAlgorithms.HmacSha256,
            Now,
            Now.AddMinutes(-1),
            Now.AddMinutes(1));

        Assert.Equal("key", accepted.KeyId);
        Assert.Same(matching, accepted.VerificationKey);
    }

    [Fact]
    public void SigningKeyMetadataValidatesCredentialIdentifiers()
    {
        SymmetricSecurityKey verification = Symmetric();

        Assert.Throws<ArgumentNullException>(() => new AccessTokenSigningKey(
            "key",
            null!,
            verification,
            Now));

        SymmetricSecurityKey mismatchedSigningKey = Symmetric();
        mismatchedSigningKey.KeyId = "other";
        SigningCredentials mismatchedCredentials = new(
            mismatchedSigningKey,
            SecurityAlgorithms.HmacSha256);
        Assert.Throws<ArgumentException>(() => new AccessTokenSigningKey(
            "key",
            mismatchedCredentials,
            Symmetric(),
            Now));

        SymmetricSecurityKey signingKey = Symmetric();
        SigningCredentials credentials = new(
            signingKey,
            SecurityAlgorithms.HmacSha256);
        AccessTokenSigningKey accepted = new(
            "key",
            credentials,
            Symmetric(),
            Now);

        Assert.Equal("key", signingKey.KeyId);
        Assert.Same(credentials, accepted.SigningCredentials);
    }

    [Fact]
    public void ConfiguredRingRejectsHostModeAndMissingMaterial()
    {
        AuthOptions hostOptions = new();
        hostOptions.AccessTokenSigning.UseHostKeyRing = true;
        Assert.Throws<InvalidOperationException>(() =>
            new ConfiguredAccessTokenSigningKeyRing(Options.Create(hostOptions), TestOptions.Clock));

        AuthOptions emptyOptions = new();
        emptyOptions.JwtSigningKey = string.Empty;
        Assert.Throws<InvalidOperationException>(() =>
            new ConfiguredAccessTokenSigningKeyRing(Options.Create(emptyOptions), TestOptions.Clock));
    }

    [Fact]
    public void ConfiguredRingRequiresAnExplicitActiveKeyWhenSeveralExist()
    {
        AuthOptions options = HmacOptions();
        options.AccessTokenSigning.HmacSha256Keys["previous"] = new HmacAccessTokenSigningKeyOptions
        {
            Key = Secret(33),
            ActivatedUtc = TestOptions.Now.AddDays(-2),
            RetiredUtc = TestOptions.Now.AddDays(1)
        };
        options.AccessTokenSigning.ActiveKeyId = string.Empty;

        Assert.Throws<InvalidOperationException>(() =>
            new ConfiguredAccessTokenSigningKeyRing(Options.Create(options), TestOptions.Clock));

        options.AccessTokenSigning.ActiveKeyId = "missing";
        Assert.Throws<InvalidOperationException>(() =>
            new ConfiguredAccessTokenSigningKeyRing(Options.Create(options), TestOptions.Clock));
    }

    [Fact]
    public void ConfiguredRingAutoSelectsOneKeyAndDisposeIsIdempotent()
    {
        AuthOptions options = HmacOptions();
        options.AccessTokenSigning.ActiveKeyId = string.Empty;

        using ConfiguredAccessTokenSigningKeyRing ring =
            new(Options.Create(options), TestOptions.Clock);

        Assert.Equal("current", ring.ActiveSigningKey.KeyId);
        Assert.Single(ring.VerificationKeys);

        ring.Dispose();
        ring.Dispose();
    }

    [Fact]
    public void ConfiguredRingSupportsLegacyPlainTextAndBase64Keys()
    {
        AuthOptions plainTextOptions = new()
        {
            JwtSigningKey = "0123456789abcdef-0123456789abcdef"
        };
        using ConfiguredAccessTokenSigningKeyRing plainText =
            new(Options.Create(plainTextOptions), TestOptions.Clock);
        Assert.StartsWith(
            "legacy-hs256-",
            plainText.ActiveSigningKey.KeyId,
            StringComparison.Ordinal);

        AuthOptions base64Options = new()
        {
            JwtSigningKey = Secret(1)
        };
        using ConfiguredAccessTokenSigningKeyRing base64 =
            new(Options.Create(base64Options), TestOptions.Clock);
        Assert.StartsWith(
            "legacy-hs256-",
            base64.ActiveSigningKey.KeyId,
            StringComparison.Ordinal);

        Assert.NotEqual(
            plainText.ActiveSigningKey.KeyId,
            base64.ActiveSigningKey.KeyId);
    }

    [Fact]
    public void RingGuardRejectsMissingEmptyDuplicateAndMismatchedCollections()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AccessTokenKeyRingGuard.Validate(null!, Now));

        AccessTokenSigningKey active = Signing("active", Now.AddMinutes(-1));

        Assert.Throws<ArgumentNullException>(() =>
            AccessTokenKeyRingGuard.Validate(
                new TestRing(null!, [active]),
                Now));

        Assert.Throws<ArgumentNullException>(() =>
            AccessTokenKeyRingGuard.Validate(
                new TestRing(active, null!),
                Now));

        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.Validate(
                new TestRing(active, []),
                Now));

        AccessTokenVerificationKey duplicate = Verification(
            "active",
            Now.AddMinutes(-2));
        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.Validate(
                new TestRing(active, [active, duplicate]),
                Now));

        AccessTokenVerificationKey different = Verification(
            "different",
            Now.AddMinutes(-2));
        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.Validate(
                new TestRing(active, [different]),
                Now));

        using RSA rsa = RSA.Create(2048);
        RsaSecurityKey rsaKey = new(rsa) { KeyId = "active" };
        AccessTokenVerificationKey wrongAlgorithm = new(
            "active",
            rsaKey,
            SecurityAlgorithms.RsaSha256,
            Now.AddMinutes(-2));
        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.Validate(
                new TestRing(active, [wrongAlgorithm]),
                Now));
    }

    [Theory]
    [MemberData(nameof(RejectedActiveWindows))]
    public void RingGuardRejectsInactiveActiveSigningKeys(
        DateTimeOffset activatedUtc,
        DateTimeOffset? notBeforeUtc,
        DateTimeOffset? retiredUtc)
    {
        AccessTokenSigningKey active = Signing(
            "active",
            activatedUtc,
            notBeforeUtc,
            retiredUtc);

        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.Validate(
                new TestRing(active, [active]),
                Now));
    }

    [Fact]
    public void RingGuardAcceptsCurrentSymmetricRsaAndEcdsaKeys()
    {
        AccessTokenSigningKey active = Signing(
            "active",
            Now.AddMinutes(-1),
            Now.AddMinutes(-2),
            Now.AddMinutes(1));
        AccessTokenKeyRingGuard.Validate(
            new TestRing(active, [active]),
            Now);

        using RSA rsa = RSA.Create(2048);
        RsaSecurityKey rsaKey = new(rsa) { KeyId = "rsa" };
        AccessTokenSigningKey rsaSigning = new(
            "rsa",
            new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256),
            rsaKey,
            Now.AddMinutes(-1));
        AccessTokenKeyRingGuard.Validate(
            new TestRing(rsaSigning, [rsaSigning]),
            Now);

        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECDsaSecurityKey ecdsaKey = new(ecdsa) { KeyId = "ecdsa" };
        AccessTokenSigningKey ecdsaSigning = new(
            "ecdsa",
            new SigningCredentials(ecdsaKey, SecurityAlgorithms.EcdsaSha256),
            ecdsaKey,
            Now.AddMinutes(-1));
        AccessTokenKeyRingGuard.Validate(
            new TestRing(ecdsaSigning, [ecdsaSigning]),
            Now);
    }

    [Fact]
    public void KeyGuardRejectsUnsupportedWeakAndConfusedKeyTypes()
    {
        AccessTokenVerificationKey unsupported = new(
            "unsupported",
            Symmetric(),
            SecurityAlgorithms.HmacSha384,
            Now);
        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.ValidateKey(
                unsupported,
                requirePrivateSigningMaterial: false,
                now: Now));

        AccessTokenVerificationKey weakHmac = new(
            "weak",
            new SymmetricSecurityKey(new byte[16]),
            SecurityAlgorithms.HmacSha256,
            Now);
        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.ValidateKey(
                weakHmac,
                requirePrivateSigningMaterial: false,
                now: Now));

        RsaSecurityKey weakRsaKey = new(new RSAParameters
        {
            Modulus = new byte[128],
            Exponent = [1, 0, 1]
        });
        AccessTokenVerificationKey weakRsa = new(
            "rsa",
            weakRsaKey,
            SecurityAlgorithms.RsaSha256,
            Now);
        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.ValidateKey(
                weakRsa,
                requirePrivateSigningMaterial: false,
                now: Now));

        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        AccessTokenVerificationKey wrongCurve = new(
            "ecdsa",
            new ECDsaSecurityKey(p384),
            SecurityAlgorithms.EcdsaSha256,
            Now);
        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.ValidateKey(
                wrongCurve,
                requirePrivateSigningMaterial: false,
                now: Now));

        AccessTokenVerificationKey confused = new(
            "confused",
            Symmetric(),
            SecurityAlgorithms.RsaSha256,
            Now);
        Assert.Throws<InvalidOperationException>(() =>
            AccessTokenKeyRingGuard.ValidateKey(
                confused,
                requirePrivateSigningMaterial: false,
                now: Now));
    }

    [Theory]
    [MemberData(nameof(AcceptanceWindows))]
    public void AcceptanceWindowChecksEachBoundary(
        DateTimeOffset activatedUtc,
        DateTimeOffset? notBeforeUtc,
        DateTimeOffset? retiredUtc,
        bool expected)
    {
        AccessTokenVerificationKey key = Verification(
            "key",
            activatedUtc,
            notBeforeUtc,
            retiredUtc);

        Assert.Equal(
            expected,
            AccessTokenKeyRingGuard.IsAccepted(key, Now));
    }

    public static TheoryData<DateTimeOffset, DateTimeOffset?, DateTimeOffset?>
        RejectedActiveWindows => new()
        {
            { Now.AddMinutes(1), null, null },
            { Now.AddMinutes(-2), Now.AddMinutes(1), null },
            { Now.AddMinutes(-2), null, Now },
            { Now.AddMinutes(-2), null, Now.AddMinutes(-1) }
        };

    public static TheoryData<DateTimeOffset, DateTimeOffset?, DateTimeOffset?, bool>
        AcceptanceWindows => new()
        {
            { Now, null, null, true },
            { Now.AddMinutes(1), null, null, false },
            { Now.AddMinutes(-1), Now, null, true },
            { Now.AddMinutes(-1), Now.AddMinutes(1), null, false },
            { Now.AddMinutes(-1), null, Now.AddMinutes(1), true },
            { Now.AddMinutes(-1), null, Now, false }
        };

    private static AuthOptions HmacOptions()
    {
        AuthOptions options = new()
        {
            JwtSigningKey = string.Empty
        };
        options.AccessTokenSigning.HmacSha256Keys["current"] =
            new HmacAccessTokenSigningKeyOptions
            {
                Key = Secret(1),
                ActivatedUtc = TestOptions.Now.AddMinutes(-1)
            };
        return options;
    }

    private static string Secret(int start) =>
        Convert.ToBase64String(
            Enumerable.Range(start, 32)
                .Select(static value => (byte)value)
                .ToArray());

    private static SymmetricSecurityKey Symmetric() =>
        new(Enumerable.Range(1, 32)
            .Select(static value => (byte)value)
            .ToArray());

    private static AccessTokenVerificationKey Verification(
        string keyId,
        DateTimeOffset activatedUtc,
        DateTimeOffset? notBeforeUtc = null,
        DateTimeOffset? retiredUtc = null) =>
        new(
            keyId,
            Symmetric(),
            SecurityAlgorithms.HmacSha256,
            activatedUtc,
            notBeforeUtc,
            retiredUtc);

    private static AccessTokenSigningKey Signing(
        string keyId,
        DateTimeOffset activatedUtc,
        DateTimeOffset? notBeforeUtc = null,
        DateTimeOffset? retiredUtc = null)
    {
        SymmetricSecurityKey key = Symmetric();
        return new AccessTokenSigningKey(
            keyId,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            key,
            activatedUtc,
            notBeforeUtc,
            retiredUtc);
    }

    private sealed class TestRing(
        AccessTokenSigningKey activeSigningKey,
        IReadOnlyCollection<AccessTokenVerificationKey> verificationKeys)
        : IAccessTokenSigningKeyRing
    {
        public AccessTokenSigningKey ActiveSigningKey { get; } =
            activeSigningKey;

        public IReadOnlyCollection<AccessTokenVerificationKey>
            VerificationKeys { get; } = verificationKeys;
    }
}
