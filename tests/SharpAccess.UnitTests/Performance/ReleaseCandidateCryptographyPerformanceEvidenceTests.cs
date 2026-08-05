using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using SharpAccess;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Security;
using SharpAccess.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests.Performance;

public sealed class ReleaseCandidateCryptographyPerformanceEvidenceTests
{
    // Produces bounded cryptography, token, and authorization-context measurements for release review.
    [Fact]
    [Trait("Evidence", "Performance")]
    public async Task CryptographyOperationsProduceReleaseCandidateEvidence()
    {
        string? outputDirectory = Environment.GetEnvironmentVariable("SHARPACCESS_PERFORMANCE_OUTPUT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        int iterations = ReadPositiveInteger("SHARPACCESS_PERFORMANCE_UNIT_ITERATIONS", 25, 1, 2_000);
        int warmupIterations = ReadPositiveInteger("SHARPACCESS_PERFORMANCE_WARMUP_ITERATIONS", 3, 1, 10);
        AuthOptions options = TestOptions.Create();
        Argon2idPasswordHasher hasher = new(Options.Create(options));
        const string password = "ValidPassword123";

        string encoded = await hasher.HashAsync(password);
        for (int index = 0; index < warmupIterations; index++)
        {
            _ = await hasher.HashAsync(password);
            PasswordVerificationStatus warmupStatus = await hasher.VerifyAsync(password, encoded);
            Assert.Equal(PasswordVerificationStatus.Success, warmupStatus);
        }

        PerformanceMetric hash = await MeasureAsync(
            "password_hash",
            Math.Max(3, Math.Min(iterations, 10)),
            async iteration => { _ = await hasher.HashAsync(password); });
        PerformanceMetric verify = await MeasureAsync(
            "password_verify",
            Math.Max(3, Math.Min(iterations, 20)),
            async _ =>
            {
                PasswordVerificationStatus status = await hasher.VerifyAsync(password, encoded);
                Assert.Equal(PasswordVerificationStatus.Success, status);
            });

        JwtAccessTokenService tokenService = new(Options.Create(options), TestOptions.Clock);
        UserContext user = CreateUserContext();
        string token = tokenService.Create(user).Token;
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };
        TokenValidationParameters validation = CreateValidationParameters(options);
        _ = handler.ValidateToken(token, validation, out _);
        for (int index = 0; index < warmupIterations; index++)
        {
            _ = tokenService.Create(user);
            _ = handler.ValidateToken(token, validation, out _);
            _ = CreateUserContext();
        }

        PerformanceMetric signing = Measure(
            "jwt_sign",
            Math.Max(100, iterations * 20),
            iteration => { _ = tokenService.Create(user); });
        PerformanceMetric validationMetric = Measure(
            "jwt_validate",
            Math.Max(100, iterations * 20),
            iteration => { _ = handler.ValidateToken(token, validation, out _); });
        PerformanceMetric authorizationContext = Measure(
            "authorization_context_construction",
            Math.Max(1_000, iterations * 100),
            iteration => { _ = CreateUserContext(); });
        PerformanceMetric queueSaturation = await MeasurePasswordHashQueueSaturationAsync(
            warmupIterations,
            Math.Max(5, Math.Min(iterations, 10)));
        PerformanceMetric noWaitRejection = await MeasurePasswordHashNoWaitRejectionAsync(
            warmupIterations,
            Math.Max(5, Math.Min(iterations, 10)));

        int encodedAccessTokenBytes = Encoding.UTF8.GetByteCount(token);
        Assert.InRange(encodedAccessTokenBytes, 1, options.SecurityLimits.MaximumEncodedAccessTokenBytes);

        var record = new
        {
            schemaVersion = 2,
            category = "cryptography-and-token",
            warmupIterations,
            configuredArgon2MemoryKiB = options.Passwords.MemorySizeKiB,
            configuredMaximumConcurrentPasswordHashes = options.Passwords.MaximumConcurrentPasswordHashes,
            configuredMaximumQueuedPasswordHashes = options.Passwords.MaximumQueuedPasswordHashes,
            encodedAccessTokenBytes,
            maximumEncodedAccessTokenBytes = options.SecurityLimits.MaximumEncodedAccessTokenBytes,
            metrics = new[]
            {
                hash,
                verify,
                queueSaturation,
                noWaitRejection,
                signing,
                validationMetric,
                authorizationContext
            },
            completedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        string json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "cryptography.json"), json);
    }

    // Creates one stable bounded authorization context for repeatable token measurements.
    private static UserContext CreateUserContext()
    {
        Guid tenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        return new UserContext(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "performance@example.test",
            true,
            new EffectiveAuthorizationContext(
                new GlobalAuthorizationContext(
                    [AuthRoles.Admin, AuthRoles.Manager],
                    [AuthPermissions.UsersRead, AuthPermissions.RolesRead, AuthPermissions.AuditRead]),
                new TenantAuthorizationContext(
                    tenantId,
                    IsOwner: true,
                    [TenantAuthRoles.Owner, TenantAuthRoles.Manager],
                    [TenantAuthPermissions.TenantRead, TenantAuthPermissions.MembersRead, TenantAuthPermissions.MembersManage]),
                AuthorizationVersion: 7),
            SecurityVersion: 7);
    }

    // Creates strict signature, issuer, audience, lifetime, algorithm, and key validation for the fixed test clock.
    private static TokenValidationParameters CreateValidationParameters(AuthOptions options)
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(options.JwtSigningKey));
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = options.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, _, _) =>
                expires.HasValue
                && expires.Value > TestOptions.Now.UtcDateTime
                && (!notBefore.HasValue || notBefore.Value <= TestOptions.Now.UtcDateTime)
        };
    }

    // Measures one synchronous operation without retaining caller-controlled values.
    private static PerformanceMetric Measure(string name, int iterations, Action<int> operation)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        double[] samples = new double[iterations];

        for (int index = 0; index < iterations; index++)
        {
            long started = Stopwatch.GetTimestamp();
            operation(index);
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        long allocatedAfter = GC.GetTotalAllocatedBytes(true);
        long workingSetAfter = Process.GetCurrentProcess().WorkingSet64;
        return CreateMetric(name, samples, allocatedAfter - allocatedBefore, workingSetAfter - workingSetBefore);
    }

    // Measures one asynchronous operation without serializing secrets or raw tokens.
    private static async Task<PerformanceMetric> MeasureAsync(
        string name,
        int iterations,
        Func<int, Task> operation)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        double[] samples = new double[iterations];

        for (int index = 0; index < iterations; index++)
        {
            long started = Stopwatch.GetTimestamp();
            await operation(index);
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        long allocatedAfter = GC.GetTotalAllocatedBytes(true);
        long workingSetAfter = Process.GetCurrentProcess().WorkingSet64;
        return CreateMetric(name, samples, allocatedAfter - allocatedBefore, workingSetAfter - workingSetBefore);
    }

    // Converts raw duration samples into bounded aggregate evidence.
    private static PerformanceMetric CreateMetric(
        string name,
        double[] samples,
        long allocatedBytes,
        long workingSetDeltaBytes)
    {
        Array.Sort(samples);
        int p95Index = Math.Clamp((int)Math.Ceiling(samples.Length * 0.95) - 1, 0, samples.Length - 1);
        return new PerformanceMetric(
            name,
            samples.Length,
            Math.Round(samples.Average(), 4),
            Math.Round(samples[samples.Length / 2], 4),
            Math.Round(samples[p95Index], 4),
            Math.Round(samples[^1], 4),
            Math.Max(0, allocatedBytes / samples.Length),
            workingSetDeltaBytes);
    }


    // Measures bounded password-hash queue wait behavior after capacity is saturated.
    private static async Task<PerformanceMetric> MeasurePasswordHashQueueSaturationAsync(
        int warmupIterations,
        int iterations)
    {
        PasswordSecurityOptions options = new()
        {
            MaximumConcurrentPasswordHashes = 1,
            MaximumQueuedPasswordHashes = 1,
            PasswordHashQueueTimeout = TimeSpan.FromSeconds(5)
        };
        PasswordHashConcurrencyLimiter limiter = PasswordHashConcurrencyLimiter.Get(options);
        for (int index = 0; index < warmupIterations; index++)
        {
            _ = await RunQueueSaturationIterationAsync(limiter);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        double[] samples = new double[iterations];
        for (int index = 0; index < iterations; index++)
        {
            samples[index] = await RunQueueSaturationIterationAsync(limiter);
        }

        long allocatedAfter = GC.GetTotalAllocatedBytes(true);
        long workingSetAfter = Process.GetCurrentProcess().WorkingSet64;
        return CreateMetric(
            "password_hash_queue_saturation",
            samples,
            allocatedAfter - allocatedBefore,
            workingSetAfter - workingSetBefore);
    }

    // Measures immediate fail-closed behavior when no password-hash queue is configured.
    private static async Task<PerformanceMetric> MeasurePasswordHashNoWaitRejectionAsync(
        int warmupIterations,
        int iterations)
    {
        PasswordSecurityOptions options = new()
        {
            MaximumConcurrentPasswordHashes = 1,
            MaximumQueuedPasswordHashes = 0,
            PasswordHashQueueTimeout = TimeSpan.FromSeconds(5)
        };
        PasswordHashConcurrencyLimiter limiter = PasswordHashConcurrencyLimiter.Get(options);
        for (int index = 0; index < warmupIterations; index++)
        {
            _ = await RunNoWaitRejectionIterationAsync(limiter);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        double[] samples = new double[iterations];
        for (int index = 0; index < iterations; index++)
        {
            samples[index] = await RunNoWaitRejectionIterationAsync(limiter);
        }

        long allocatedAfter = GC.GetTotalAllocatedBytes(true);
        long workingSetAfter = Process.GetCurrentProcess().WorkingSet64;
        return CreateMetric(
            "password_hash_no_wait_rejection",
            samples,
            allocatedAfter - allocatedBefore,
            workingSetAfter - workingSetBefore);
    }

    // Runs one deterministic queue saturation acquisition and release cycle.
    private static async Task<double> RunQueueSaturationIterationAsync(
        PasswordHashConcurrencyLimiter limiter)
    {
        PasswordHashConcurrencyLimiter.Lease occupied =
            await limiter.AcquireAsync(CancellationToken.None);
        try
        {
            long started = Stopwatch.GetTimestamp();
            Task<PasswordHashConcurrencyLimiter.Lease> queued =
                limiter.AcquireAsync(CancellationToken.None).AsTask();
            await WaitForQueuedCountAsync(limiter, expected: 1);
            occupied.Dispose();
            await using PasswordHashConcurrencyLimiter.Lease acquired = await queued;
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        finally
        {
            occupied.Dispose();
        }
    }

    // Runs one deterministic no-wait rejection while the only slot is occupied.
    private static async Task<double> RunNoWaitRejectionIterationAsync(
        PasswordHashConcurrencyLimiter limiter)
    {
        await using PasswordHashConcurrencyLimiter.Lease occupied =
            await limiter.AcquireAsync(CancellationToken.None);
        long started = Stopwatch.GetTimestamp();
        try
        {
            await using PasswordHashConcurrencyLimiter.Lease unexpected =
                await limiter.AcquireAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        throw new InvalidOperationException("The no-wait password-hash limiter unexpectedly accepted queued work.");
    }

    // Waits until the deterministic limiter reports the expected queued operation.
    private static async Task WaitForQueuedCountAsync(
        PasswordHashConcurrencyLimiter limiter,
        int expected)
    {
        for (int attempt = 0; attempt < 1_000; attempt++)
        {
            if (limiter.QueuedCount == expected)
            {
                return;
            }

            await Task.Delay(1);
        }

        throw new TimeoutException("The password-hash limiter did not reach the expected queued count.");
    }
    // Reads one bounded positive integer from the release-candidate environment.
    private static int ReadPositiveInteger(string name, int fallback, int minimum, int maximum)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
    }

    private sealed record PerformanceMetric(
        string Name,
        int Iterations,
        double MeanMilliseconds,
        double P50Milliseconds,
        double P95Milliseconds,
        double MaximumMilliseconds,
        long AllocatedBytesPerOperation,
        long WorkingSetDeltaBytes);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
