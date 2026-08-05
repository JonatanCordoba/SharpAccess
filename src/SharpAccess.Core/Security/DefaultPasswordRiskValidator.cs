using System.Globalization;
using SharpAccess;

namespace SharpAccess.Security;

internal sealed class DefaultPasswordRiskValidator : IPasswordRiskValidator
{
    private static readonly HashSet<string> CommonPasswords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "password",
            "password1",
            "password123",
            "qwerty123",
            "admin123",
            "letmein123",
            "welcome123",
            "changeme123",
            "dotnetauth123"
        };

    // Rejects common or account-derived candidates before expensive hashing.
    public ValueTask<bool> IsAllowedAsync(
        string password,
        string? normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();
        string candidate = password.Trim();
        if (candidate.Length == 0 || CommonPasswords.Contains(candidate))
        {
            return ValueTask.FromResult(false);
        }

        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            string local = normalizedEmail.Split('@', 2)[0];
            if (local.Length >= 4
                && candidate.ToUpper(CultureInfo.InvariantCulture).Contains(local, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }
        }

        return ValueTask.FromResult(true);
    }
}
