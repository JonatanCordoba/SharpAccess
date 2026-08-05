namespace SharpAccess;

/// <summary>Allows hosts to reject weak, common, breached, or context-derived passwords before hashing.</summary>
public interface IPasswordRiskValidator
{
    /// <summary>Returns whether the candidate password may be accepted for the normalized account identifier.</summary>
    /// <param name="password">The candidate password. Implementations must not persist or log it.</param>
    /// <param name="normalizedEmail">The optional normalized account identifier used to detect context-derived passwords.</param>
    /// <param name="cancellationToken">A token that cancels asynchronous risk checks.</param>
    /// <returns>A value task whose result is true only when the password passes every configured risk check.</returns>
    ValueTask<bool> IsAllowedAsync(
        string password,
        string? normalizedEmail,
        CancellationToken cancellationToken = default);
}
