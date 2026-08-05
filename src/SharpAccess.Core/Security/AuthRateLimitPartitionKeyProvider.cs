using System.Security.Cryptography;
using System.Text;
using SharpAccess.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SharpAccess;

/// <summary>Creates privacy-preserving partition identifiers for local or host-owned distributed rate limiters.</summary>
public interface IAuthRateLimitPartitionKeyProvider
{
    /// <summary>Creates a privacy-preserving partition identifier for one authentication operation.</summary>
    /// <param name="httpContext">The current HTTP context, used to observe the remote address.</param>
    /// <param name="operation">The stable authentication operation name.</param>
    /// <param name="normalizedAccount">An optional normalized account identifier.</param>
    /// <param name="category">An optional additional partition category.</param>
    /// <returns>A keyed identifier that does not expose the raw IP address, account, or category.</returns>
    /// <exception cref="System.ArgumentNullException">httpContext is null.</exception>
    /// <exception cref="System.ArgumentException">operation is null, empty, or whitespace.</exception>
    string CreatePartitionKey(
        HttpContext httpContext,
        string operation,
        string? normalizedAccount = null,
        string? category = null);
}

internal sealed class AuthRateLimitPartitionKeyProvider : IAuthRateLimitPartitionKeyProvider, IDisposable
{
    private readonly byte[] _key;
    private bool _disposed;

    /// <summary>Loads the mandatory dedicated partition secret and validates its decoded strength.</summary>
    public AuthRateLimitPartitionKeyProvider(IOptions<AuthOptions> configuredOptions)
    {
        AuthOptions options = configuredOptions.Value;
        string material = options.RateLimits.PartitionKey;
        if (string.IsNullOrWhiteSpace(material))
        {
            throw new InvalidOperationException("A rate-limit partition key is required when authentication rate limits are enabled.");
        }

        _key = Decode(material);
        if (_key.Length < 32)
        {
            CryptographicOperations.ZeroMemory(_key);
            throw new InvalidOperationException("The rate-limit partition key must contain at least 32 bytes.");
        }
    }

    /// <summary>Creates a keyed partition from the operation, observed IP, account, and category.</summary>
    public string CreatePartitionKey(
        HttpContext httpContext,
        string operation,
        string? normalizedAccount = null,
        string? category = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string ipHash = Hash(ip);
        string accountHash = string.IsNullOrWhiteSpace(normalizedAccount)
            ? "none"
            : Hash(normalizedAccount);
        string safeCategory = string.IsNullOrWhiteSpace(category)
            ? "none"
            : Hash(category);
        return string.Concat(operation, "|", ipHash, "|", accountHash, "|", safeCategory);
    }

    /// <summary>Clears partition-key material when the provider is disposed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>Produces a truncated HMAC that keeps raw partition dimensions private.</summary>
    private string Hash(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            Span<byte> digest = stackalloc byte[32];
            HMACSHA256.HashData(_key, bytes, digest);
            return Convert.ToHexString(digest[..12]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Decodes Base64 material or treats a non-Base64 secret as UTF-8 bytes.</summary>
    private static byte[] Decode(string value)
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
}
