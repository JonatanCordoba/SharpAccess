namespace SharpAccess.Abstractions;

internal interface IAuthClock
{
    DateTimeOffset UtcNow { get; }
}

internal sealed class SystemAuthClock : IAuthClock
{
    // Returns the current UTC time.
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
