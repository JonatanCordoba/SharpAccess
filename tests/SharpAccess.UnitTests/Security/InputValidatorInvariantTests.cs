using SharpAccess.Security;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class InputValidatorInvariantTests
{
    private static readonly string OverlongEmail =
        new string('a', 310) + "@example.com";

    private static readonly string OverlongPassword =
        new string('a', 256) + "1";

    private static readonly string OverlongSlug =
        new string('a', 81);

    private static readonly string OverlongReturnUrl =
        "/" + new string('a', 2_048);

    [Fact]
    public void EmailValidationRejectsMaximumLengthOverflow()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateEmail(
            OverlongEmail,
            out string normalizedEmail);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalizedEmail);
    }

    [Fact]
    public void PasswordValidationRejectsNullAndMaximumLengthOverflow()
    {
        InputValidator validator = CreateValidator();

        Assert.False(validator.IsValidPassword(null));
        Assert.False(validator.IsValidPassword(OverlongPassword));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NameValidationRejectsMissingNames(string? value)
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateName(
            value,
            maximumLength: 32,
            out string normalizedName);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalizedName);
    }

    [Fact]
    public void NameValidationRejectsInvalidMaximumLength()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateName(
            "Admin",
            maximumLength: 0,
            out string normalizedName);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalizedName);
    }

    [Fact]
    public void NameValidationRejectsNamesBeyondMaximumLength()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateName(
            "Administrators",
            maximumLength: 5,
            out string normalizedName);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalizedName);
    }

    [Fact]
    public void NameValidationRejectsCommaDelimitedNames()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateName(
            "Admin,User",
            maximumLength: 32,
            out string normalizedName);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalizedName);
    }

    [Fact]
    public void NameValidationRejectsControlCharacters()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateName(
            "Admin\u0001User",
            maximumLength: 32,
            out string normalizedName);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalizedName);
    }

    [Fact]
    public void NameValidationTrimsAndNormalizesValidNames()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateName(
            "  Tenant Administrator  ",
            maximumLength: 32,
            out string normalizedName);

        Assert.True(accepted);
        Assert.Equal("TENANT ADMINISTRATOR", normalizedName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SlugValidationRejectsMissingValues(string? value)
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateSlug(
            value,
            out string normalizedSlug);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalizedSlug);
    }

    [Fact]
    public void SlugValidationRejectsMaximumLengthOverflow()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateSlug(
            OverlongSlug,
            out string normalizedSlug);

        Assert.False(accepted);
        Assert.Equal(string.Empty, normalizedSlug);
    }

    [Fact]
    public void SlugValidationTrimsAndNormalizesValidValues()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateSlug(
            "  Tenant-42  ",
            out string normalizedSlug);

        Assert.True(accepted);
        Assert.Equal("tenant-42", normalizedSlug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnUrlValidationDefaultsMissingValuesToRoot(string? value)
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateReturnUrl(
            value,
            out string safeReturnUrl);

        Assert.True(accepted);
        Assert.Equal("/", safeReturnUrl);
    }

    [Fact]
    public void ReturnUrlValidationRejectsMaximumLengthOverflow()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateReturnUrl(
            OverlongReturnUrl,
            out string safeReturnUrl);

        Assert.False(accepted);
        Assert.Equal("/", safeReturnUrl);
    }

    [Fact]
    public void ReturnUrlValidationRejectsFragments()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateReturnUrl(
            "/account#profile",
            out string safeReturnUrl);

        Assert.False(accepted);
        Assert.Equal("/", safeReturnUrl);
    }

    [Fact]
    public void ReturnUrlValidationRejectsControlCharacters()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateReturnUrl(
            "/account\u0001profile",
            out string safeReturnUrl);

        Assert.False(accepted);
        Assert.Equal("/", safeReturnUrl);
    }

    [Fact]
    public void ReturnUrlValidationPreservesSafeQueryStrings()
    {
        InputValidator validator = CreateValidator();

        bool accepted = validator.TryValidateReturnUrl(
            "/account?tab=security",
            out string safeReturnUrl);

        Assert.True(accepted);
        Assert.Equal("/account?tab=security", safeReturnUrl);
    }

    private static InputValidator CreateValidator() =>
        new(Options.Create(TestOptions.Create()));
}
