using System.Globalization;
using SharpAccess.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
[Trait("Capability", "PaginationContract")]
public sealed class SqliteQueryPlanTests
{
    // Verifies every bounded SQLite list query can use its matching keyset index.
    [Fact]
    public async Task BoundedListQueriesReferenceMatchingKeysetIndexes()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sharpaccess-query-plan-{Guid.NewGuid():N}");
        string database = Path.Combine(root, "auth.db");
        string connectionString = $"Data Source={database};Pooling=False";
        Directory.CreateDirectory(root);
        try
        {
            await InitializeDatabaseAsync(connectionString);

            await using SqliteConnection connection = new(connectionString);
            await connection.OpenAsync();

            await AssertPlanUsesIndexAsync(
                connection,
                """
                SELECT id,created_utc
                FROM auth_users INDEXED BY ix_auth_users_created
                WHERE created_utc < $afterCreated
                   OR (created_utc = $afterCreated AND id > $afterId)
                ORDER BY created_utc DESC,id ASC
                LIMIT $limit;
                """,
                "ix_auth_users_created");

            await AssertPlanUsesIndexAsync(
                connection,
                """
                SELECT id,created_utc
                FROM auth_security_audit_logs INDEXED BY ix_auth_audit_created
                WHERE created_utc < $afterCreated
                   OR (created_utc = $afterCreated AND id > $afterId)
                ORDER BY created_utc DESC,id ASC
                LIMIT $limit;
                """,
                "ix_auth_audit_created");

            await AssertPlanUsesIndexAsync(
                connection,
                """
                SELECT id,created_utc
                FROM auth_global_roles INDEXED BY ix_auth_global_roles_page
                WHERE created_utc < $afterCreated
                   OR (created_utc = $afterCreated AND id > $afterId)
                ORDER BY created_utc DESC,id ASC
                LIMIT $limit;
                """,
                "ix_auth_global_roles_page");

            await AssertPlanUsesIndexAsync(
                connection,
                """
                SELECT id,created_utc
                FROM auth_global_permissions INDEXED BY ix_auth_global_permissions_page
                WHERE created_utc < $afterCreated
                   OR (created_utc = $afterCreated AND id > $afterId)
                ORDER BY created_utc DESC,id ASC
                LIMIT $limit;
                """,
                "ix_auth_global_permissions_page");

            await AssertScopedPlanUsesIndexAsync(
                connection,
                """
                SELECT tenant_id,created_utc
                FROM auth_tenant_memberships INDEXED BY ix_auth_tenant_memberships_user_page
                WHERE user_id = $scopeId
                  AND (created_utc < $afterCreated
                    OR (created_utc = $afterCreated AND tenant_id > $afterId))
                ORDER BY created_utc DESC,tenant_id ASC
                LIMIT $limit;
                """,
                "ix_auth_tenant_memberships_user_page");

            await AssertScopedPlanUsesIndexAsync(
                connection,
                """
                SELECT user_id,created_utc
                FROM auth_tenant_memberships INDEXED BY ix_auth_tenant_memberships_tenant_page
                WHERE tenant_id = $scopeId
                  AND (created_utc < $afterCreated
                    OR (created_utc = $afterCreated AND user_id > $afterId))
                ORDER BY created_utc DESC,user_id ASC
                LIMIT $limit;
                """,
                "ix_auth_tenant_memberships_tenant_page");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // Applies the real provider migration sequence to the query-plan fixture.
    private static async Task InitializeDatabaseAsync(string connectionString)
    {
        ServiceCollection services = new();
        services.AddSqliteAccess(options => options.ConnectionString = connectionString);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        using IServiceScope scope = provider.CreateScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        await store.InitializeAsync().ConfigureAwait(false);
    }

    // Verifies an unscoped keyset query references its required index.
    private static Task AssertPlanUsesIndexAsync(
        SqliteConnection connection,
        string query,
        string indexName) =>
        AssertPlanUsesIndexAsync(connection, query, indexName, includeScope: false);

    // Verifies a scoped keyset query references its required index.
    private static Task AssertScopedPlanUsesIndexAsync(
        SqliteConnection connection,
        string query,
        string indexName) =>
        AssertPlanUsesIndexAsync(connection, query, indexName, includeScope: true);

    // Executes EXPLAIN QUERY PLAN with bounded cursor parameters and checks the selected index.
    private static async Task AssertPlanUsesIndexAsync(
        SqliteConnection connection,
        string query,
        string indexName,
        bool includeScope)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + query;
        command.Parameters.AddWithValue(
            "$afterCreated",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$afterId", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$limit", 51);
        if (includeScope)
        {
            command.Parameters.AddWithValue("$scopeId", Guid.NewGuid().ToString("D"));
        }

        List<string> details = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            details.Add(reader.GetString(3));
        }

        string plan = string.Join(Environment.NewLine, details);
        Assert.Contains(indexName, plan, StringComparison.Ordinal);
    }
}
