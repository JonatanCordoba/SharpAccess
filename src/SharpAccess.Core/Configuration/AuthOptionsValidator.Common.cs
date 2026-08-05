using System.Security.Cryptography;
using System.Text;

namespace SharpAccess.Configuration;

internal sealed partial class AuthOptionsValidator
{
    private const string HttpTokenSeparators = "()<>@,;:\\\"/[]?={}";

    // Requires strong dedicated partition material and rejects all cross-purpose reuse.
    private static void ValidateRateLimitPartitionKey(
        AuthOptions options,
        bool isProduction,
        List<string> failures)
    {
        Dictionary<string, string> isolatedFingerprint = new(StringComparer.Ordinal);
        ValidateSecret(
            options.RateLimits.PartitionKey,
            "RateLimits.PartitionKey",
            32,
            isProduction,
            isolatedFingerprint,
            failures);
        if (string.IsNullOrWhiteSpace(options.RateLimits.PartitionKey))
        {
            return;
        }

        foreach ((string field, string? value) in EnumerateOtherSecrets(options))
        {
            if (!string.IsNullOrWhiteSpace(value)
                && SecretMaterialEquals(options.RateLimits.PartitionKey, value))
            {
                failures.Add($"RateLimits.PartitionKey must be dedicated and must not reuse secret material from {field}.");
            }
        }
    }

    // Enumerates every secret whose material must remain distinct from the partition key.
    private static IEnumerable<(string Field, string? Value)> EnumerateOtherSecrets(AuthOptions options)
    {
        IEnumerable<(string Field, string? Value)> otherSecrets =
        [
            (nameof(options.JwtSigningKey), options.JwtSigningKey),
            ("TokenHashing.Key", options.TokenHashing.Key)
        ];
        return otherSecrets
            .Concat(options.AccessTokenSigning.HmacSha256Keys?.Select(static pair =>
                ($"AccessTokenSigning.HmacSha256Keys[{pair.Key}]", pair.Value?.Key))
                ?? [])
            .Concat(options.TokenHashing.Keys?.Select(static pair =>
                ($"TokenHashing.Keys[{pair.Key}]", (string?)pair.Value))
                ?? [])
            .Concat(options.Passwords.Peppers?.Select(static pair =>
                ($"Passwords.Peppers[{pair.Key}]", (string?)pair.Value))
                ?? [])
            .Concat(options.OpenIdConnect.Providers.Select(static pair =>
                ($"OpenIdConnect.Providers[{pair.Key}].ClientSecret", pair.Value?.ClientSecret)));
    }

    // Compares decoded secret material in fixed time when lengths match.
    private static bool SecretMaterialEquals(string first, string second)
    {
        byte[] firstBytes = DecodeSecret(first);
        byte[] secondBytes = DecodeSecret(second);
        try
        {
            return firstBytes.Length == secondBytes.Length
                && CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstBytes);
            CryptographicOperations.ZeroMemory(secondBytes);
        }
    }

    // Decodes Base64 secret material or falls back to its UTF-8 representation.
    private static byte[] DecodeSecret(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(value);
        }
    }

    // Validates migration mode and generated-script output requirements.
    private static void ValidateMigrationOptions(
        SharpAccessMigrationOptions options,
        List<string> failures)
    {
        if (options.Mode.HasValue && !Enum.IsDefined(options.Mode.Value))
        {
            failures.Add("Migrations.Mode is not supported.");
            return;
        }

        if (options.Mode != SharpAccessMigrationMode.GenerateScript)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ScriptOutputPath))
        {
            failures.Add("Migrations.ScriptOutputPath is required in GenerateScript mode.");
            return;
        }

        if (options.ScriptOutputPath.Length > 4_096
            || options.ScriptOutputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            failures.Add("Migrations.ScriptOutputPath must be a valid path no longer than 4096 characters.");
        }
    }

    // Validates only the rate limits associated with mapped feature endpoints.
    private static void ValidateRateLimits(
        AuthFeatureOptions features,
        AuthRateLimitOptions options,
        bool hasExternalAuthentication,
        List<string> failures)
    {
        if (features.PasswordAuthentication)
        {
            RequireRange(options.LoginPerMinute, 1, 10_000, "RateLimits.LoginPerMinute", failures);
        }

        if (features.Registration)
        {
            RequireRange(options.RegisterPerMinute, 1, 10_000, "RateLimits.RegisterPerMinute", failures);
            RequireRange(options.EmailVerificationPerMinute, 1, 10_000, "RateLimits.EmailVerificationPerMinute", failures);
        }

        if (features.RefreshTokens)
        {
            RequireRange(options.RefreshPerMinute, 1, 10_000, "RateLimits.RefreshPerMinute", failures);
        }

        if (features.PasswordReset)
        {
            RequireRange(options.PasswordResetPerMinute, 1, 10_000, "RateLimits.PasswordResetPerMinute", failures);
        }

        if (hasExternalAuthentication)
        {
            RequireRange(options.OAuthPerMinute, 1, 10_000, "RateLimits.OAuthPerMinute", failures);
        }
    }

    // Validates secret presence, decoded length, production predictability, and material reuse.
    private static void ValidateSecret(
        string? value,
        string field,
        int minimumBytes,
        bool rejectPredictable,
        Dictionary<string, string> fingerprints,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{field} is required.");
            return;
        }

        byte[] bytes = DecodeSecret(value);
        try
        {
            if (bytes.Length < minimumBytes)
            {
                failures.Add($"{field} must contain at least {minimumBytes} bytes.");
            }

            if (rejectPredictable && IsPredictableSecret(value, bytes))
            {
                failures.Add($"{field} must not use sample, repeated, default, or predictable secret material in Production.");
            }

            if (rejectPredictable)
            {
                ValidateSecretFingerprint(bytes, field, fingerprints, failures);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    // Records one production secret fingerprint and rejects cross-purpose reuse.
    private static void ValidateSecretFingerprint(
        byte[] bytes,
        string field,
        Dictionary<string, string> fingerprints,
        List<string> failures)
    {
        string fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        if (fingerprints.TryGetValue(fingerprint, out string? otherField)
            && !string.Equals(field, otherField, StringComparison.Ordinal))
        {
            failures.Add($"{field} must not reuse secret material from {otherField}.");
            return;
        }

        fingerprints[fingerprint] = field;
    }

    // Reports whether secret material is repeated or contains a known sample marker.
    private static bool IsPredictableSecret(string value, byte[] bytes)
    {
        if (bytes.Length > 0 && bytes.All(item => item == bytes[0]))
        {
            return true;
        }

        string normalized = value.Trim().ToLowerInvariant();
        string[] predictable =
        [
            "changeme",
            "change-me",
            "default",
            "development",
            "dummy",
            "example",
            "integration",
            "placeholder",
            "password",
            "replace",
            "sample",
            "secret",
            "sharpaccess",
            "test-key",
            "token-key"
        ];
        return predictable.Any(normalized.Contains);
    }

    // Requires a nonempty text value.
    private static void RequireText(string? value, string field, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{field} is required.");
        }
    }

    // Requires a bounded identifier without whitespace, controls, or colons.
    private static void RequireVersion(string? value, string field, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character == ':'))
        {
            failures.Add($"{field} must be a nonempty identifier no longer than 128 characters without whitespace, controls, or colons.");
        }
    }

    // Requires an integer inside an inclusive range.
    private static void RequireRange(
        int value,
        int minimum,
        int maximum,
        string field,
        List<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{field} must be between {minimum} and {maximum}.");
        }
    }

    // Requires a duration inside an inclusive range.
    private static void RequireTimeSpan(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string field,
        List<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{field} must be between {minimum} and {maximum}.");
        }
    }

    // Requires an absolute HTTP URI and HTTPS for non-loopback hosts.
    private static void RequireSecureBaseUri(Uri? value, string field, List<string> failures)
    {
        if (!IsValidAbsoluteHttpUri(value))
        {
            failures.Add($"{field} must be an absolute HTTP or HTTPS URI.");
            return;
        }

        Uri baseUri = value;
        if (!baseUri.IsLoopback && baseUri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{field} must use HTTPS for non-loopback hosts.");
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            failures.Add($"{field} cannot contain credentials, a query, or a fragment.");
        }
    }

    // Requires a bounded HTTP token suitable for a cookie name.
    private static void ValidateCookieName(string? value, string field, List<string> failures)
    {
        if (!IsValidHttpToken(value))
        {
            failures.Add($"{field} must be a valid ASCII cookie name no longer than 128 characters.");
        }
    }

    // Requires a bounded HTTP token suitable for a header name.
    private static void ValidateHeaderName(string? value, string field, List<string> failures)
    {
        if (!IsValidHttpToken(value))
        {
            failures.Add($"{field} must be a valid ASCII header name no longer than 128 characters.");
        }
    }

    // Reports whether a value is a bounded nonempty HTTP token.
    private static bool IsValidHttpToken(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(IsCookieNameCharacter);

    // Reports whether one character belongs to the RFC-compatible HTTP token alphabet.
    private static bool IsCookieNameCharacter(char value) =>
        value is >= (char)0x21 and <= (char)0x7E
        && !HttpTokenSeparators.Contains(value);

    // Requires a local absolute path without unsafe delimiters or controls.
    private static void ValidateLocalPath(string? value, string field, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 1_024
            || !value.StartsWith('/')
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains('?')
            || value.Contains('#')
            || value.Contains(';')
            || value.Any(char.IsControl))
        {
            failures.Add($"{field} must be a local absolute path without delimiters, controls, a query, or a fragment.");
        }
    }

    // Requires an absolute HTTPS URI without credentials, query, or fragment.
    private static void RequireHttpsUri(Uri? value, string field, List<string> failures)
    {
        if (value is null || !value.IsAbsoluteUri || value.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{field} must be an absolute HTTPS URI.");
            return;
        }

        if (!string.IsNullOrEmpty(value.UserInfo)
            || !string.IsNullOrEmpty(value.Query)
            || !string.IsNullOrEmpty(value.Fragment))
        {
            failures.Add($"{field} cannot contain credentials, a query, or a fragment.");
        }
    }

    // Reports whether a URI is absolute HTTP or HTTPS.
    private static bool IsValidAbsoluteHttpUri(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Uri? value) =>
        value is not null
        && value.IsAbsoluteUri
        && (value.Scheme == Uri.UriSchemeHttp || value.Scheme == Uri.UriSchemeHttps);

    // Reports whether a URI is absolute HTTP or HTTPS and uses a non-loopback host.
    private static bool IsNonLoopbackAbsoluteHttpUri(Uri? value) =>
        IsValidAbsoluteHttpUri(value) && !value.IsLoopback;
}
