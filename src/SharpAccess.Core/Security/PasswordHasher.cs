using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpAccess.Configuration;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace SharpAccess.Security;

internal enum PasswordVerificationStatus
{
    Failed,
    Success,
    SuccessNeedsRehash
}

internal interface IPasswordHasher
{
    Task<string> HashAsync(string password, CancellationToken cancellationToken = default);
    Task<PasswordVerificationStatus> VerifyAsync(
        string password,
        string encodedHash,
        CancellationToken cancellationToken = default);
}

internal interface IDummyPasswordHashProvider
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<string> GetAsync(CancellationToken cancellationToken = default);
}

internal sealed class DummyPasswordHashProvider : IDummyPasswordHashProvider
{
    private const string DummyPassword = "SharpAccess-Dummy-Password-12345";
    private readonly Lazy<Task<string>> _hash;

    public DummyPasswordHashProvider(IPasswordHasher passwordHasher)
    {
        _hash = new Lazy<Task<string>>(
            () => passwordHasher.HashAsync(DummyPassword, CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _hash.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<string> GetAsync(CancellationToken cancellationToken = default) =>
        _hash.Value.WaitAsync(cancellationToken);
}

internal sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const string Algorithm = "argon2id";
    private const string AlgorithmVersion = "v=19";
    private readonly PasswordSecurityOptions _settings;
    private readonly PasswordHashConcurrencyLimiter _limiter;

    public Argon2idPasswordHasher(IOptions<AuthOptions> options)
    {
        _settings = options.Value.Passwords;
        _limiter = PasswordHashConcurrencyLimiter.Get(_settings);
    }

    public async Task<string> HashAsync(string password, CancellationToken cancellationToken = default)
    {
        ValidatePasswordInput(password);
        byte[] salt = RandomNumberGenerator.GetBytes(_settings.SaltSizeBytes);
        try
        {
            byte[] hash = await DeriveAsync(
                password,
                salt,
                _settings.CurrentPepperVersion,
                _settings.Iterations,
                _settings.MemorySizeKiB,
                _settings.DegreeOfParallelism,
                _settings.HashSizeBytes,
                cancellationToken).ConfigureAwait(false);
            try
            {
                return string.Join(
                    '$',
                    Algorithm,
                    AlgorithmVersion,
                    $"m={_settings.MemorySizeKiB},t={_settings.Iterations},p={_settings.DegreeOfParallelism}",
                    $"pepper={_settings.CurrentPepperVersion}",
                    Convert.ToBase64String(salt),
                    Convert.ToBase64String(hash));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public async Task<PasswordVerificationStatus> VerifyAsync(
        string password,
        string encodedHash,
        CancellationToken cancellationToken = default)
    {
        if (IsInvalidVerificationInput(password, encodedHash)
            || !TryParse(encodedHash, out ParsedPasswordHash? parsed))
        {
            return PasswordVerificationStatus.Failed;
        }

        try
        {
            return await VerifyParsedAsync(password, parsed, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(parsed.Salt);
            CryptographicOperations.ZeroMemory(parsed.Hash);
        }
    }

    private bool IsInvalidVerificationInput(string password, string encodedHash) =>
        string.IsNullOrEmpty(password)
        || password.Length > _settings.MaximumLength
        || string.IsNullOrWhiteSpace(encodedHash);

    private async Task<PasswordVerificationStatus> VerifyParsedAsync(
        string password,
        ParsedPasswordHash parsed,
        CancellationToken cancellationToken)
    {
        if (!_settings.Peppers.ContainsKey(parsed.PepperVersion))
        {
            return PasswordVerificationStatus.Failed;
        }

        byte[] actual = await DeriveAsync(
            password,
            parsed.Salt,
            parsed.PepperVersion,
            parsed.Iterations,
            parsed.MemorySizeKiB,
            parsed.Parallelism,
            parsed.Hash.Length,
            cancellationToken).ConfigureAwait(false);
        bool matches;
        try
        {
            matches = CryptographicOperations.FixedTimeEquals(actual, parsed.Hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }

        if (!matches)
        {
            return PasswordVerificationStatus.Failed;
        }

        bool needsRehash = NeedsRehash(parsed);
        if (needsRehash)
        {
            SharpAccessSecurityMetrics.PasswordHashesRequiringRehash.Add(1);
        }

        return needsRehash
            ? PasswordVerificationStatus.SuccessNeedsRehash
            : PasswordVerificationStatus.Success;
    }

    private bool NeedsRehash(ParsedPasswordHash parsed) =>
        parsed.PepperVersion != _settings.CurrentPepperVersion
        || parsed.Iterations != _settings.Iterations
        || parsed.MemorySizeKiB != _settings.MemorySizeKiB
        || parsed.Parallelism != _settings.DegreeOfParallelism
        || parsed.Salt.Length != _settings.SaltSizeBytes
        || parsed.Hash.Length != _settings.HashSizeBytes;

    private void ValidatePasswordInput(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < _settings.MinimumLength || password.Length > _settings.MaximumLength)
        {
            throw new ArgumentException(
                $"Password length must be between {_settings.MinimumLength} and {_settings.MaximumLength} characters.",
                nameof(password));
        }
    }

    private async Task<byte[]> DeriveAsync(
        string password,
        byte[] salt,
        string pepperVersion,
        int iterations,
        int memorySizeKiB,
        int parallelism,
        int hashSizeBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using PasswordHashConcurrencyLimiter.Lease lease =
            await _limiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        long started = Stopwatch.GetTimestamp();
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] pepperBytes = [];
        byte[] input = [];
        try
        {
            pepperBytes = Encoding.UTF8.GetBytes(_settings.Peppers[pepperVersion]);
            input = GC.AllocateUninitializedArray<byte>(passwordBytes.Length + pepperBytes.Length);
            passwordBytes.CopyTo(input, 0);
            pepperBytes.CopyTo(input, passwordBytes.Length);
            using Argon2id argon2 = new(input)
            {
                Salt = salt,
                Iterations = iterations,
                MemorySize = memorySizeKiB,
                DegreeOfParallelism = parallelism
            };
            byte[] result = await argon2.GetBytesAsync(hashSizeBytes).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                CryptographicOperations.ZeroMemory(result);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return result;
        }
        finally
        {
            SharpAccessSecurityMetrics.PasswordHashDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(pepperBytes);
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static bool TryParse(string value, [NotNullWhen(true)] out ParsedPasswordHash? parsed)
    {
        parsed = null;
        if (!TryReadEncodedParts(value, out string[] parts)
            || !TryReadMetadata(
                parts,
                out int memory,
                out int iterations,
                out int parallelism,
                out string pepperVersion)
            || !TryDecodeHashMaterial(parts, out byte[] salt, out byte[] hash))
        {
            return false;
        }

        parsed = new ParsedPasswordHash(
            memory,
            iterations,
            parallelism,
            pepperVersion,
            salt,
            hash);
        return true;
    }

    private static bool TryReadEncodedParts(string value, out string[] parts)
    {
        parts = value.Split('$', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 6
            && string.Equals(parts[0], Algorithm, StringComparison.Ordinal)
            && string.Equals(parts[1], AlgorithmVersion, StringComparison.Ordinal);
    }

    private static bool TryReadMetadata(
        string[] parts,
        out int memory,
        out int iterations,
        out int parallelism,
        out string pepperVersion)
    {
        pepperVersion = string.Empty;
        if (!TryParseParameters(parts[2], out memory, out iterations, out parallelism)
            || !parts[3].StartsWith("pepper=", StringComparison.Ordinal))
        {
            return false;
        }

        pepperVersion = parts[3][7..];
        return !string.IsNullOrWhiteSpace(pepperVersion);
    }

    private static bool TryDecodeHashMaterial(
        string[] parts,
        out byte[] salt,
        out byte[] hash)
    {
        salt = [];
        hash = [];
        try
        {
            salt = Convert.FromBase64String(parts[4]);
            hash = Convert.FromBase64String(parts[5]);
            if (IsValidHashMaterial(salt, hash))
            {
                return true;
            }

            ZeroHashMaterial(salt, hash);
            salt = [];
            hash = [];
            return false;
        }
        catch (FormatException)
        {
            ZeroHashMaterial(salt, hash);
            salt = [];
            hash = [];
            return false;
        }
    }

    private static bool IsValidHashMaterial(byte[] salt, byte[] hash) =>
        salt.Length is >= 16 and <= 64
        && hash.Length is >= 32 and <= 64;

    private static void ZeroHashMaterial(byte[] salt, byte[] hash)
    {
        CryptographicOperations.ZeroMemory(salt);
        CryptographicOperations.ZeroMemory(hash);
    }

    private static bool TryParseParameters(
        string value,
        out int memory,
        out int iterations,
        out int parallelism)
    {
        memory = 0;
        iterations = 0;
        parallelism = 0;
        string[] parameters = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parameters.Length != 3)
        {
            return false;
        }

        bool parsed = TryParseNamedInteger(parameters[0], "m=", out memory)
            && TryParseNamedInteger(parameters[1], "t=", out iterations)
            && TryParseNamedInteger(parameters[2], "p=", out parallelism);
        return parsed
            && memory is >= 8_192 and <= 262_144
            && iterations is >= 1 and <= 10
            && parallelism is >= 1 and <= 32;
    }

    private static bool TryParseNamedInteger(string value, string prefix, out int result)
    {
        result = 0;
        return value.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private sealed record ParsedPasswordHash(
        int MemorySizeKiB,
        int Iterations,
        int Parallelism,
        string PepperVersion,
        byte[] Salt,
        byte[] Hash);
}
