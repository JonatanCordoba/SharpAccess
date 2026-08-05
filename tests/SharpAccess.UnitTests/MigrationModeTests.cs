using SharpAccess.Configuration;
using SharpAccess.Persistence;

namespace SharpAccess.UnitTests;

public sealed class MigrationModeTests
{
    // Verifies that Development and Test preserve zero-infrastructure automatic migration behavior.
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void LocalEnvironmentsDefaultToApplyAtStartup(string environmentName)
    {
        Assert.Equal(
            SharpAccessMigrationMode.ApplyAtStartup,
            SharpAccessMigrationModeResolver.Resolve(null, environmentName));
    }

    // Verifies that Production and unknown environments default to read-only validation.
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Custom")]
    public void NonLocalEnvironmentsDefaultToValidateOnly(string environmentName)
    {
        Assert.Equal(
            SharpAccessMigrationMode.ValidateOnly,
            SharpAccessMigrationModeResolver.Resolve(null, environmentName));
    }

    // Verifies that every explicit host selection overrides the environment default.
    [Theory]
    [InlineData(SharpAccessMigrationMode.ApplyAtStartup)]
    [InlineData(SharpAccessMigrationMode.ValidateOnly)]
    [InlineData(SharpAccessMigrationMode.External)]
    [InlineData(SharpAccessMigrationMode.GenerateScript)]
    public void ExplicitModeWins(SharpAccessMigrationMode mode)
    {
        Assert.Equal(mode, SharpAccessMigrationModeResolver.Resolve(mode, "Production"));
    }

    [Fact]
    public void MissingEnvironmentDefaultsToValidateOnly()
    {
        Assert.Equal(
            SharpAccessMigrationMode.ValidateOnly,
            SharpAccessMigrationModeResolver.Resolve(null, null));
    }
}
