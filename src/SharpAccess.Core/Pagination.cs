namespace SharpAccess;

/// <summary>Describes one bounded request for a cursor-paginated SharpAccess collection.</summary>
/// <param name="Cursor">The opaque continuation cursor returned by a previous page, or null for the first page.</param>
/// <param name="Limit">The requested maximum item count. Consumers should keep it between DefaultLimit and MaximumLimit.</param>
public sealed record SharpAccessPageRequest(
    string? Cursor = null,
    int Limit = SharpAccessPageRequest.DefaultLimit)
{
    /// <summary>Gets the default number of items requested for one page.</summary>
    public const int DefaultLimit = 100;
    /// <summary>Gets the maximum number of items accepted for one page.</summary>
    public const int MaximumLimit = 200;
}

/// <summary>Returns one bounded collection page and an opaque continuation cursor when more data exists.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items returned for this page.</param>
/// <param name="NextCursor">The opaque cursor for the next page, or null when no further page exists.</param>
public sealed record SharpAccessPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor);
