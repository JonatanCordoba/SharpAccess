using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SharpAccess.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SharpAccess.Security;

internal sealed class CompositePasswordRiskValidator(
    DefaultPasswordRiskValidator local,
    BreachedPasswordRiskValidator breached) : IPasswordRiskValidator
{
    public async ValueTask<bool> IsAllowedAsync(
        string password,
        string? normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await local.IsAllowedAsync(password, normalizedEmail, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return await breached.IsAllowedAsync(password, normalizedEmail, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class BreachedPasswordRiskValidator : IPasswordRiskValidator, IDisposable
{
    internal const string HttpClientName = "SharpAccess.BreachedPasswords";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BreachedPasswordOptions _options;
    private readonly MemoryCache _cache;
    private readonly object _circuitGate = new();
    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenUntil;
    private bool _disposed;

    public BreachedPasswordRiskValidator(
        IHttpClientFactory httpClientFactory,
        IOptions<AuthOptions> configuredOptions)
    {
        _httpClientFactory = httpClientFactory;
        _options = configuredOptions.Value.Passwords.BreachedPasswords;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _options.MaximumCacheEntries });
    }

    public async ValueTask<bool> IsAllowedAsync(
        string password,
        string? normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        _ = normalizedEmail;
        if (!_options.Enabled)
        {
            return true;
        }

        string digest = ComputeRangeDigestHex(password);
        if (_cache.TryGetValue(digest, out bool allowed))
        {
            return allowed;
        }

        if (IsCircuitOpen())
        {
            return _options.FailureMode == BreachedPasswordFailureMode.FailOpen;
        }

        try
        {
            bool breached = await QueryAsync(digest, cancellationToken).ConfigureAwait(false);
            lock (_circuitGate)
            {
                _consecutiveFailures = 0;
                _circuitOpenUntil = default;
            }

            allowed = !breached;
            _cache.Set(
                digest,
                allowed,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _options.CacheDuration,
                    Size = 1
                });
            return allowed;
        }
        catch (HttpRequestException)
        {
            RegisterFailure();
            return _options.FailureMode == BreachedPasswordFailureMode.FailOpen;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RegisterFailure();
            return _options.FailureMode == BreachedPasswordFailureMode.FailOpen;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cache.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task<bool> QueryAsync(string digest, CancellationToken cancellationToken)
    {
        string prefix = digest[..5];
        string suffix = digest[5..];
        Uri endpoint = new(_options.Endpoint, prefix);
        using CancellationTokenSource timeout = new(_options.Timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        request.Headers.Add("Add-Padding", "true");
        using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                $"The breached-password range endpoint returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        await response.Content.LoadIntoBufferAsync(_options.MaximumResponseBytes, linked.Token).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        foreach (string line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            if (line.AsSpan(0, separator).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                && count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsCircuitOpen()
    {
        lock (_circuitGate)
        {
            return _circuitOpenUntil > DateTimeOffset.UtcNow;
        }
    }

    private void RegisterFailure()
    {
        lock (_circuitGate)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= _options.CircuitBreakerFailureThreshold)
            {
                _circuitOpenUntil = DateTimeOffset.UtcNow.Add(_options.CircuitBreakerDuration);
                _consecutiveFailures = 0;
            }
        }
    }

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "The range-service digest is protocol-required and is never used for password storage or authentication.")]
    [SuppressMessage(
        "Security",
        "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "The range-service digest is protocol-required and is never used for password storage or authentication.")]
    // The range service mandates this legacy digest only for a k-anonymity prefix; it never protects stored credentials.
    private static string ComputeRangeDigestHex(string password)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(password);
        byte[] digest = SHA1.HashData(bytes); // DevSkim: ignore DS126858
        try
        {
            return Convert.ToHexString(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
