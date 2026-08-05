using SharpAccess.Security;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class InputValidatorEdgeInvariantTests
{
    [Fact]
    public void TenantSlugRejectsBlankAndOversizedValues()
    {
        InputValidator validator = new(Options.Create(TestOptions.Create()));

        Assert.False(validator.TryValidateSlug(" ", out string blankSlug));
        Assert.Equal(string.Empty, blankSlug);
        Assert.False(validator.TryValidateSlug(new string('a', 81), out string longSlug));
        Assert.Equal(string.Empty, longSlug);
    }

    [Fact]
    public void ReturnUrlDefaultsBlankValuesToRoot()
    {
        InputValidator validator = new(Options.Create(TestOptions.Create()));

        Assert.True(validator.TryValidateReturnUrl(null, out string nullSafeUrl));
        Assert.Equal("/", nullSafeUrl);
        Assert.True(validator.TryValidateReturnUrl("   ", out string blankSafeUrl));
        Assert.Equal("/", blankSafeUrl);
    }
}
