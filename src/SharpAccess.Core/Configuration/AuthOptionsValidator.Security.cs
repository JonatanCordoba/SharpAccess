namespace SharpAccess.Configuration;

internal sealed partial class AuthOptionsValidator
{
    // Validates configured signing material and active-key windows against the injected clock.
    private void ValidateSigningOptions(
        AuthOptions options,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        DateTimeOffset now = _clock.UtcNow;
        AccessTokenSigningOptions signing = options.AccessTokenSigning;
        if (signing.HmacSha256Keys is null)
        {
            failures.Add("AccessTokenSigning.HmacSha256Keys cannot be null.");
            return;
        }

        if (signing.UseHostKeyRing)
        {
            ValidateHostKeyRingSelection(options, signing, failures);
            return;
        }

        if (signing.HmacSha256Keys.Count == 0)
        {
            ValidateLegacySigningKey(options, isProduction, secretFingerprints, failures);
            return;
        }

        ValidateConfiguredSigningRing(signing, now, isProduction, secretFingerprints, failures);
    }

    // Rejects configured HMAC material when the host owns the signing key ring.
    private static void ValidateHostKeyRingSelection(
        AuthOptions options,
        AccessTokenSigningOptions signing,
        List<string> failures)
    {
        if (signing.HmacSha256Keys!.Count > 0 || !string.IsNullOrWhiteSpace(options.JwtSigningKey))
        {
            failures.Add("AccessTokenSigning.UseHostKeyRing cannot be combined with configured HMAC signing material.");
        }
    }

    // Validates the migration-only single signing key when no versioned ring exists.
    private static void ValidateLegacySigningKey(
        AuthOptions options,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        ValidateSecret(
            options.JwtSigningKey,
            nameof(options.JwtSigningKey),
            32,
            isProduction,
            secretFingerprints,
            failures);
        if (isProduction && !string.IsNullOrWhiteSpace(options.JwtSigningKey))
        {
            failures.Add("JwtSigningKey is a migration-only single-key setting; Production must use AccessTokenSigning or a host key ring.");
        }
    }

    // Validates active-key selection, ring bounds, and every configured key.
    private static void ValidateConfiguredSigningRing(
        AccessTokenSigningOptions signing,
        DateTimeOffset now,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        RequireText(signing.ActiveKeyId, "AccessTokenSigning.ActiveKeyId", failures);
        if (string.IsNullOrWhiteSpace(signing.ActiveKeyId)
            || !signing.HmacSha256Keys!.ContainsKey(signing.ActiveKeyId))
        {
            failures.Add("AccessTokenSigning.HmacSha256Keys must contain ActiveKeyId.");
        }

        if (signing.HmacSha256Keys.Count > 16)
        {
            failures.Add("AccessTokenSigning.HmacSha256Keys cannot contain more than 16 keys.");
        }

        foreach ((string keyId, HmacAccessTokenSigningKeyOptions key) in signing.HmacSha256Keys)
        {
            ValidateSigningKey(
                keyId,
                key,
                signing.ActiveKeyId,
                now,
                isProduction,
                secretFingerprints,
                failures);
        }
    }

    // Validates one versioned signing key and its active-key window when selected.
    private static void ValidateSigningKey(
        string keyId,
        HmacAccessTokenSigningKeyOptions? key,
        string? activeKeyId,
        DateTimeOffset now,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        RequireVersion(keyId, "AccessTokenSigning key identifier", failures);
        if (key is null)
        {
            failures.Add($"AccessTokenSigning.HmacSha256Keys[{keyId}] cannot be null.");
            return;
        }

        ValidateSecret(
            key.Key,
            $"AccessTokenSigning.HmacSha256Keys[{keyId}]",
            32,
            isProduction,
            secretFingerprints,
            failures);
        if (key.NotBeforeUtc.HasValue
            && key.RetiredUtc.HasValue
            && key.NotBeforeUtc.Value >= key.RetiredUtc.Value)
        {
            failures.Add($"AccessTokenSigning.HmacSha256Keys[{keyId}] has an invalid validity window.");
        }

        if (string.Equals(keyId, activeKeyId, StringComparison.Ordinal))
        {
            ValidateActiveSigningKeyWindow(key, now, failures);
        }
    }

    // Rejects an active key that is not currently usable.
    private static void ValidateActiveSigningKeyWindow(
        HmacAccessTokenSigningKeyOptions key,
        DateTimeOffset now,
        List<string> failures)
    {
        if (key.ActivatedUtc > now)
        {
            failures.Add("AccessTokenSigning.ActiveKeyId cannot identify a key whose activation time is in the future.");
        }

        if (key.NotBeforeUtc.HasValue && key.NotBeforeUtc.Value > now)
        {
            failures.Add("AccessTokenSigning.ActiveKeyId cannot identify a key whose not-before time is in the future.");
        }

        if (key.RetiredUtc.HasValue && key.RetiredUtc.Value <= now)
        {
            failures.Add("AccessTokenSigning.ActiveKeyId cannot identify a retired key.");
        }
    }

    // Validates the current pepper selection and every accepted pepper value.
    private static void ValidatePasswordPeppers(
        PasswordSecurityOptions passwords,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        if (passwords.Peppers is null)
        {
            failures.Add("Passwords.Peppers cannot be null.");
            return;
        }

        if (string.IsNullOrWhiteSpace(passwords.CurrentPepperVersion)
            || !passwords.Peppers.ContainsKey(passwords.CurrentPepperVersion))
        {
            failures.Add("Passwords.Peppers must contain the current pepper version.");
        }

        foreach ((string version, string pepper) in passwords.Peppers)
        {
            RequireVersion(version, "Passwords.Peppers version", failures);
            ValidateSecret(
                pepper,
                $"Passwords.Peppers[{version}]",
                16,
                isProduction,
                secretFingerprints,
                failures);
        }
    }

    // Validates versioned token-hashing material and legacy-hash compatibility.
    private static void ValidateTokenHashingOptions(
        TokenHashingOptions options,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        RequireVersion(options.CurrentKeyVersion, "TokenHashing.CurrentKeyVersion", failures);
        if (options.Keys is null)
        {
            failures.Add("TokenHashing.Keys cannot be null.");
            return;
        }

        if (options.Keys.Count == 0)
        {
            ValidateSecret(
                options.Key,
                "TokenHashing.Key",
                32,
                isProduction,
                secretFingerprints,
                failures);
            return;
        }

        ValidateTokenHashingSelections(options, failures);
        foreach ((string version, string key) in options.Keys)
        {
            RequireVersion(version, "TokenHashing key version", failures);
            ValidateSecret(
                key,
                $"TokenHashing.Keys[{version}]",
                32,
                isProduction,
                secretFingerprints,
                failures);
        }
    }

    // Validates selected token-hashing versions and accepted-ring bounds.
    private static void ValidateTokenHashingSelections(
        TokenHashingOptions options,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.CurrentKeyVersion)
            || !options.Keys!.ContainsKey(options.CurrentKeyVersion))
        {
            failures.Add("TokenHashing.Keys must contain CurrentKeyVersion.");
        }

        if (!string.IsNullOrWhiteSpace(options.LegacyUnversionedKeyVersion))
        {
            RequireVersion(
                options.LegacyUnversionedKeyVersion,
                "TokenHashing.LegacyUnversionedKeyVersion",
                failures);
            if (!options.Keys.ContainsKey(options.LegacyUnversionedKeyVersion))
            {
                failures.Add("TokenHashing.Keys must contain LegacyUnversionedKeyVersion while legacy hashes are accepted.");
            }
        }

        if (options.Keys.Count > 16)
        {
            failures.Add("TokenHashing.Keys cannot contain more than 16 accepted versions.");
        }
    }

    // Validates bounded token, refresh-family, and refresh-token security limits.
    private static void ValidateSecurityLimits(
        AuthSecurityLimitOptions options,
        List<string> failures)
    {
        RequireRange(options.MaximumRolesPerToken, 1, 256, "SecurityLimits.MaximumRolesPerToken", failures);
        RequireRange(options.MaximumPermissionsPerToken, 1, 1_024, "SecurityLimits.MaximumPermissionsPerToken", failures);
        RequireRange(
            options.MaximumEncodedAccessTokenBytes,
            1_024,
            65_536,
            "SecurityLimits.MaximumEncodedAccessTokenBytes",
            failures);
        RequireRange(
            options.MaximumActiveRefreshFamiliesPerUser,
            1,
            1_000,
            "SecurityLimits.MaximumActiveRefreshFamiliesPerUser",
            failures);
        RequireRange(
            options.MaximumActiveRefreshTokensPerFamily,
            1,
            1_000,
            "SecurityLimits.MaximumActiveRefreshTokensPerFamily",
            failures);
    }

    // Validates breached-password failure mode, endpoint, resilience, cache, and payload bounds.
    private static void ValidateBreachedPasswordOptions(
        BreachedPasswordOptions options,
        List<string> failures)
    {
        if (!Enum.IsDefined(options.FailureMode))
        {
            failures.Add("Passwords.BreachedPasswords.FailureMode is not supported.");
        }

        if (options.Enabled)
        {
            RequireHttpsUri(options.Endpoint, "Passwords.BreachedPasswords.Endpoint", failures);
        }

        RequireTimeSpan(
            options.Timeout,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(30),
            "Passwords.BreachedPasswords.Timeout",
            failures);
        RequireRange(
            options.CircuitBreakerFailureThreshold,
            1,
            100,
            "Passwords.BreachedPasswords.CircuitBreakerFailureThreshold",
            failures);
        RequireTimeSpan(
            options.CircuitBreakerDuration,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromHours(1),
            "Passwords.BreachedPasswords.CircuitBreakerDuration",
            failures);
        RequireRange(
            options.MaximumCacheEntries,
            1,
            100_000,
            "Passwords.BreachedPasswords.MaximumCacheEntries",
            failures);
        RequireRange(
            options.MaximumResponseBytes,
            1_024,
            8_388_608,
            "Passwords.BreachedPasswords.MaximumResponseBytes",
            failures);
        RequireTimeSpan(
            options.CacheDuration,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromDays(7),
            "Passwords.BreachedPasswords.CacheDuration",
            failures);
    }
}
