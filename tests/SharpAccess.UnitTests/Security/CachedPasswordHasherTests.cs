using SharpAccess.Security;

namespace SharpAccess.UnitTests;

public sealed class CachedPasswordHasherTests
{
    [Fact]
    public async Task CachedProviderReturnsTheSameValue()
    {
        CountingHasher hasher = new();
        DummyPasswordHashProvider provider = new(hasher);

        await provider.InitializeAsync();
        string first = await provider.GetAsync();
        string second = await provider.GetAsync();

        Assert.Equal("cached-value", first);
        Assert.Equal(first, second);
        Assert.Equal(1, hasher.Calls);
    }

    private sealed class CountingHasher : IPasswordHasher
    {
        public int Calls { get; private set; }

        public Task<string> HashAsync(string value, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult("cached-value");
        }

        public Task<PasswordVerificationStatus> VerifyAsync(
            string value,
            string encodedHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PasswordVerificationStatus.Success);
    }
}
