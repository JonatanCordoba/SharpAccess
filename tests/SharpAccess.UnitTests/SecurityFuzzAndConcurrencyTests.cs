using SharpAccess.Configuration;
using SharpAccess.Security;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class SecurityFuzzAndConcurrencyTests
{
    // Verifies that a deterministic malformed-hash corpus always fails closed.
    [Fact]
    public async Task MalformedPasswordHashCorpusFailsClosed()
    {
        Argon2idPasswordHasher hasher = new(Options.Create(TestOptions.Create()));
        Random random = new(0x5A17C0DE);
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789$=,+/-_";
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            int length = random.Next(1, 160);
            char[] value = new char[length];
            for (int index = 0; index < value.Length; index++) value[index] = alphabet[random.Next(alphabet.Length)];
            string malformed = "fuzz-" + new string(value);
            PasswordVerificationStatus status = await hasher.VerifyAsync("ValidPassword123", malformed);
            Assert.Equal(PasswordVerificationStatus.Failed, status);
        }
    }

    // Verifies that concurrent dummy-hash requests execute the expensive initialization once.
    [Fact]
    public async Task DummyPasswordHashProviderInitializesOnceUnderConcurrency()
    {
        CountingPasswordHasher hasher = new();
        DummyPasswordHashProvider provider = new(hasher);
        Task<string>[] requests = Enumerable.Range(0, 64).Select(_ => provider.GetAsync()).ToArray();
        string[] results = await Task.WhenAll(requests);
        Assert.All(results, value => Assert.Equal("stable-dummy-hash", value));
        Assert.Equal(1, hasher.HashCalls);
    }

    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        private int _hashCalls;
        public int HashCalls => Volatile.Read(ref _hashCalls);
        // Counts one hash operation while simulating asynchronous work.
        public async Task<string> HashAsync(string password, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _hashCalls);
            await Task.Delay(25, cancellationToken);
            return "stable-dummy-hash";
        }
        // Returns a deterministic failed verification result for the concurrency fixture.
        public Task<PasswordVerificationStatus> VerifyAsync(string password, string encodedHash, CancellationToken cancellationToken = default) => Task.FromResult(PasswordVerificationStatus.Failed);
    }
}
