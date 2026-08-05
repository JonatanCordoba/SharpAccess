using Microsoft.AspNetCore.DataProtection;
using SharpAccess.Persistence;
using SharpAccess.Services;

namespace SharpAccess.UnitTests.Services;

public sealed class AuthPageCursorCodecTests
{
    private static readonly DateTimeOffset BoundaryUtc = new(2026, 7, 17, 12, 30, 45, TimeSpan.Zero);
    private static readonly Guid BoundaryId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    // Verifies the stable public page-size contract.
    [Fact]
    public void PageRequestUsesDocumentedBounds()
    {
        SharpAccessPageRequest request = new();

        Assert.Equal(100, request.Limit);
        Assert.Equal(100, SharpAccessPageRequest.DefaultLimit);
        Assert.Equal(200, SharpAccessPageRequest.MaximumLimit);
    }

    // Verifies providers clamp defensively and fetch exactly one lookahead row.
    [Theory]
    [InlineData(-10, 1, 2)]
    [InlineData(25, 25, 26)]
    [InlineData(500, 200, 201)]
    public void ProviderFetchLimitIsDefensivelyBounded(int requested, int expectedPage, int expectedFetch)
    {
        int fetch = AuthPageSupport.GetFetchLimit(
            new AuthPageQuery(requested, null),
            out int pageLimit);

        Assert.Equal(expectedPage, pageLimit);
        Assert.Equal(expectedFetch, fetch);
    }

    // Verifies the continuation boundary is the last emitted item rather than the lookahead item.
    [Fact]
    public void ProviderSliceUsesLastEmittedBoundaryAndExcludesLookahead()
    {
        AuthPageBoundary first = new(BoundaryUtc, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        AuthPageBoundary second = new(BoundaryUtc, Guid.Parse("00000000-0000-0000-0000-000000000002"));
        AuthPageBoundary lookahead = new(BoundaryUtc, Guid.Parse("00000000-0000-0000-0000-000000000003"));

        AuthPageSlice<string> page = AuthPageSupport.CreateSlice(
            new[] { ("first", first), ("second", second), ("lookahead", lookahead) },
            pageLimit: 2);

        Assert.Equal(["first", "second"], page.Items);
        Assert.Equal(second, page.Next);
    }

    // Verifies that malformed limits and cursor envelopes are rejected before a provider query exists.
    [Theory]
    [InlineData(0, null)]
    [InlineData(201, null)]
    [InlineData(10, " ")]
    [InlineData(10, "not-a-cursor")]
    [InlineData(10, "v1.not-protected")]
    public void InvalidRequestsAreRejected(int limit, string? cursor)
    {
        AuthPageCursorCodec codec = CreateCodec();

        Assert.False(codec.TryCreateQuery(
            new SharpAccessPageRequest(cursor, limit),
            AuthPageCursorCodec.UsersScope,
            null,
            out _));
    }

    // Verifies that oversized cursor input is bounded before cryptographic processing.
    [Fact]
    public void OversizedCursorIsRejected()
    {
        AuthPageCursorCodec codec = CreateCodec();
        string oversized = "v1." + new string('a', AuthPageCursorCodec.MaximumCursorLength);

        Assert.False(codec.TryCreateQuery(
            new SharpAccessPageRequest(oversized, 10),
            AuthPageCursorCodec.UsersScope,
            null,
            out _));
    }

    // Verifies protected cursor round-tripping and deterministic boundary recovery.
    [Fact]
    public void ProtectedCursorRoundTripsOnlyForItsCollectionScope()
    {
        AuthPageCursorCodec codec = CreateCodec();
        SharpAccessPage<string> page = codec.CreatePage(
            new AuthPageSlice<string>(["item"], new AuthPageBoundary(BoundaryUtc, BoundaryId)),
            AuthPageCursorCodec.UsersScope,
            null);

        Assert.NotNull(page.NextCursor);
        Assert.StartsWith("v1.", page.NextCursor, StringComparison.Ordinal);
        Assert.True(codec.TryCreateQuery(
            new SharpAccessPageRequest(page.NextCursor, 25),
            AuthPageCursorCodec.UsersScope,
            null,
            out AuthPageQuery query));
        Assert.Equal(25, query.Limit);
        Assert.Equal(BoundaryUtc, query.After?.CreatedUtc);
        Assert.Equal(BoundaryId, query.After?.Id);
        Assert.False(codec.TryCreateQuery(
            new SharpAccessPageRequest(page.NextCursor, 25),
            AuthPageCursorCodec.RolesScope,
            null,
            out _));
    }

    // Verifies that tenant and requesting-user isolation identifiers cannot be crossed.
    [Theory]
    [InlineData(AuthPageCursorCodec.TenantsScope)]
    [InlineData(AuthPageCursorCodec.TenantMembersScope)]
    public void ProtectedCursorIsBoundToItsIsolationIdentifier(string scope)
    {
        AuthPageCursorCodec codec = CreateCodec();
        Guid firstTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid secondTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SharpAccessPage<string> page = codec.CreatePage(
            new AuthPageSlice<string>(["item"], new AuthPageBoundary(BoundaryUtc, BoundaryId)),
            scope,
            firstTenant);

        Assert.False(codec.TryCreateQuery(
            new SharpAccessPageRequest(page.NextCursor, 10),
            scope,
            secondTenant,
            out _));
    }

    // Verifies a protected but unsupported payload version is rejected uniformly.
    [Fact]
    public void UnknownProtectedCursorVersionIsRejected()
    {
        EphemeralDataProtectionProvider provider = new();
        AuthPageCursorCodec codec = new(provider);
        string protectedPayload = provider.CreateProtector("SharpAccess.Pagination.v1").Protect(
            $$"""{"Version":2,"Scope":"{{AuthPageCursorCodec.UsersScope}}","ScopeId":null,"CreatedUtc":"{{BoundaryUtc:O}}","Id":"{{BoundaryId:D}}"}""");
        string cursor = "v1." + protectedPayload;

        Assert.False(codec.TryCreateQuery(
            new SharpAccessPageRequest(cursor, 10),
            AuthPageCursorCodec.UsersScope,
            null,
            out _));
    }

    // Verifies that tampering and a different Data Protection key ring invalidate a cursor.
    [Fact]
    public void ProtectedCursorRejectsTamperingAndDifferentKeyRing()
    {
        AuthPageCursorCodec first = CreateCodec();
        AuthPageCursorCodec second = CreateCodec();
        string cursor = first.CreatePage(
            new AuthPageSlice<string>(["item"], new AuthPageBoundary(BoundaryUtc, BoundaryId)),
            AuthPageCursorCodec.AuditScope,
            null).NextCursor!;
        char replacement = cursor[^1] == 'A' ? 'B' : 'A';
        string tampered = cursor[..^1] + replacement;

        Assert.False(first.TryCreateQuery(
            new SharpAccessPageRequest(tampered, 10),
            AuthPageCursorCodec.AuditScope,
            null,
            out _));
        Assert.False(second.TryCreateQuery(
            new SharpAccessPageRequest(cursor, 10),
            AuthPageCursorCodec.AuditScope,
            null,
            out _));
    }

    // Creates an isolated ephemeral Data Protection key ring for one test codec.
    private static AuthPageCursorCodec CreateCodec() =>
        new(new EphemeralDataProtectionProvider());
}
