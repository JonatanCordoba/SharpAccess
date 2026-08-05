namespace SharpAccess.Persistence;

// Identifies the stable keyset position after the last item returned to a caller.
internal sealed record AuthPageBoundary(
    DateTimeOffset CreatedUtc,
    Guid Id);

// Carries a validated bounded page request into one provider implementation.
internal sealed record AuthPageQuery(
    int Limit,
    AuthPageBoundary? After);

// Returns provider items and the last emitted position only when another page exists.
internal sealed record AuthPageSlice<T>(
    IReadOnlyList<T> Items,
    AuthPageBoundary? Next);

// Applies the same defensive provider bounds and N+1 page construction across database engines.
internal static class AuthPageSupport
{
    // Clamps a validated request again at the provider boundary and returns its N+1 fetch size.
    internal static int GetFetchLimit(AuthPageQuery query, out int pageLimit)
    {
        ArgumentNullException.ThrowIfNull(query);
        pageLimit = Math.Clamp(query.Limit, 1, SharpAccessPageRequest.MaximumLimit);
        return pageLimit + 1;
    }

    // Converts N+1 keyed rows into one bounded result and a continuation boundary when needed.
    internal static AuthPageSlice<T> CreateSlice<T>(
        IReadOnlyList<(T Item, AuthPageBoundary Boundary)> fetched,
        int pageLimit)
    {
        bool hasMore = fetched.Count > pageLimit;
        (T Item, AuthPageBoundary Boundary)[] returned = fetched.Take(pageLimit).ToArray();
        AuthPageBoundary? next = hasMore && returned.Length > 0
            ? returned[^1].Boundary
            : null;
        return new AuthPageSlice<T>(returned.Select(static row => row.Item).ToArray(), next);
    }
}
