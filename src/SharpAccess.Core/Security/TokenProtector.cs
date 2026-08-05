using System.Security.Cryptography;
using System.Text;
using SharpAccess.Configuration;
using Microsoft.Extensions.Options;

namespace SharpAccess.Security;

internal interface ITokenProtector
{
    string Generate(int byteLength = 48);
    string Hash(string rawToken);
    IReadOnlyList<string> HashCandidates(string rawToken) => new[] { Hash(rawToken) };
}

internal sealed class HmacTokenProtector : ITokenProtector, IDisposable
{
    private readonly string _activeVersion;
    private readonly KeyEntry[] _keys;
    private readonly string? _legacyUnversionedVersion;
    private bool _disposed;

    public HmacTokenProtector(IOptions<AuthOptions> options)
    {
        TokenHashingOptions configured = options.Value.TokenHashing;
        _activeVersion = configured.CurrentKeyVersion;
        List<KeyEntry> keys = [];
        if (configured.Keys.Count > 0)
        {
            foreach ((string version, string key) in configured.Keys)
            {
                keys.Add(new KeyEntry(version, Decode(key)));
            }
            _legacyUnversionedVersion = configured.LegacyUnversionedKeyVersion;
        }
        else if (!string.IsNullOrWhiteSpace(configured.Key))
        {
            keys.Add(new KeyEntry(_activeVersion, Decode(configured.Key)));
            _legacyUnversionedVersion = _activeVersion;
        }

        if (keys.Count == 0 || keys.All(entry => !string.Equals(entry.Version, _activeVersion, StringComparison.Ordinal)))
        {
            foreach (KeyEntry key in keys)
            {
                CryptographicOperations.ZeroMemory(key.Material);
            }

            throw new InvalidOperationException("TokenHashing must contain the active key version.");
        }

        string[] tags = keys.Select(static entry => CreateVersionTag(entry.Version)).ToArray();
        if (tags.Distinct(StringComparer.Ordinal).Count() != tags.Length)
        {
            foreach (KeyEntry key in keys)
            {
                CryptographicOperations.ZeroMemory(key.Material);
            }

            throw new InvalidOperationException("TokenHashing key versions produce a duplicate persisted version tag.");
        }

        _keys = keys
            .OrderByDescending(entry => string.Equals(entry.Version, _activeVersion, StringComparison.Ordinal))
            .ThenBy(static entry => entry.Version, StringComparer.Ordinal)
            .ToArray();
    }

    public string Generate(int byteLength = 48)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (byteLength is < 32 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        byte[] bytes = RandomNumberGenerator.GetBytes(byteLength);
        try
        {
            return Base64UrlEncode(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Hash(string rawToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        KeyEntry active = _keys.First(entry => string.Equals(entry.Version, _activeVersion, StringComparison.Ordinal));
        return Format(active.Version, Compute(active.Material, rawToken));
    }

    public IReadOnlyList<string> HashCandidates(string rawToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        if (rawToken.Length > 1_024)
        {
            throw new ArgumentException("Token is too long.", nameof(rawToken));
        }

        List<string> candidates = new(_keys.Length + 1);
        foreach (KeyEntry key in _keys)
        {
            string digest = Compute(key.Material, rawToken);
            candidates.Add(Format(key.Version, digest));
            if (!string.IsNullOrWhiteSpace(_legacyUnversionedVersion)
                && string.Equals(key.Version, _legacyUnversionedVersion, StringComparison.Ordinal))
            {
                candidates.Add(digest);
            }
        }

        return candidates;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (KeyEntry key in _keys)
        {
            CryptographicOperations.ZeroMemory(key.Material);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static string Compute(byte[] key, string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        if (rawToken.Length > 1_024)
        {
            throw new ArgumentException("Token is too long.", nameof(rawToken));
        }

        byte[] input = Encoding.UTF8.GetBytes(rawToken);
        try
        {
            return Convert.ToHexString(HMACSHA256.HashData(key, input));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static string Format(string version, string hash) =>
        CreateVersionTag(version) + hash[..56];

    private static string CreateVersionTag(string version)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(version);
        try
        {
            Span<byte> digest = stackalloc byte[32];
            SHA256.HashData(bytes, digest);
            return Convert.ToHexString(digest[..4]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

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

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record KeyEntry(string Version, byte[] Material);
}
