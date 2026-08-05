using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using SharpAccess.Configuration;
using Microsoft.Extensions.Options;

namespace SharpAccess.Security;

internal interface IInputValidator
{
    // Validates and normalizes an email address without accepting display names.
    bool TryValidateEmail(string? email, out string normalizedEmail);

    // Validates a password before it reaches the memory-hard hashing service.
    bool IsValidPassword(string? password);

    // Validates a role, permission, or tenant display name and produces a normalized key.
    bool TryValidateName(string? name, int maximumLength, out string normalizedName);

    // Validates and normalizes a tenant slug.
    bool TryValidateSlug(string? slug, out string normalizedSlug);

    // Restricts OAuth return URLs to local absolute paths to prevent open redirects.
    bool TryValidateReturnUrl(string? returnUrl, out string safeReturnUrl);
}

internal sealed partial class InputValidator(IOptions<AuthOptions> options) : IInputValidator
{
    private readonly PasswordSecurityOptions _passwords = options.Value.Passwords;

    // Validates and normalizes an email address without accepting display names.
    public bool TryValidateEmail(string? email, out string normalizedEmail)
    {
        normalizedEmail = string.Empty;
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320)
        {
            return false;
        }

        string trimmed = email.Trim();
        try
        {
            MailAddress address = new(trimmed);
            if (!string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalizedEmail = trimmed.ToUpperInvariant();
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // Validates a password before it reaches the memory-hard hashing service.
    public bool IsValidPassword(string? password) =>
        password is not null
        && password.Length >= _passwords.MinimumLength
        && password.Length <= _passwords.MaximumLength
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);

    // Validates a role, permission, or tenant display name and produces a normalized key.
    public bool TryValidateName(string? name, int maximumLength, out string normalizedName)
    {
        normalizedName = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        if (maximumLength < 1
            || trimmed.Length > maximumLength
            || trimmed.Contains(',')
            || trimmed.Any(char.IsControl))
        {
            return false;
        }

        normalizedName = trimmed.ToUpper(CultureInfo.InvariantCulture);
        return true;
    }

    // Validates and normalizes a tenant slug.
    public bool TryValidateSlug(string? slug, out string normalizedSlug)
    {
        normalizedSlug = string.Empty;
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 80)
        {
            return false;
        }

        string value = slug.Trim().ToLowerInvariant();
        if (!SlugPattern().IsMatch(value))
        {
            return false;
        }

        normalizedSlug = value;
        return true;
    }

    // Restricts OAuth return URLs to local absolute paths to prevent open redirects.
    public bool TryValidateReturnUrl(string? returnUrl, out string safeReturnUrl)
    {
        safeReturnUrl = "/";
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return true;
        }

        if (returnUrl.Length > 2_048
            || returnUrl[0] != '/'
            || (returnUrl.Length > 1 && returnUrl[1] == '/')
            || returnUrl.Contains('\\')
            || returnUrl.Contains('#')
            || returnUrl.Any(char.IsControl))
        {
            return false;
        }

        safeReturnUrl = returnUrl;
        return true;
    }

    // Produces the strict lowercase tenant slug pattern.
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
