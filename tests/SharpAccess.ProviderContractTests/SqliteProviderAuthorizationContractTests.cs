using SharpAccess.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]

public sealed class SqliteProviderAuthorizationContractTests : AuthProviderAuthorizationContractTestBase
{
    private string _databasePath = null!;

    // Creates the SQLite auth store used by the inherited provider-contract tests.
    protected override object CreateProviderStore()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"sharpaccess-provider-authorization-{Guid.NewGuid():N}.db");
        SqliteAuthOptions options = new() { ConnectionString = $"Data Source={_databasePath};Pooling=False" };
        return new SqliteAuthStore(new SqliteAuthConnectionFactory(Options.Create(options)));
    }

    // Removes the temporary SQLite database used by the current test.
    protected override Task DisposeProviderResourcesAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }
}
