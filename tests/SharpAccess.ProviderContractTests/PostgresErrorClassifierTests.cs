using SharpAccess.Persistence;
using SharpAccess.Postgres;
using Xunit;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Postgres")]
public sealed class PostgresErrorClassifierTests
{
    // Verifies every explicitly supported SQLSTATE class maps to its provider-neutral category.
    [Theory]
    [InlineData("08006", "ConnectionFailure")]
    [InlineData("23505", "UniqueConstraint")]
    [InlineData("23503", "ForeignKeyConstraint")]
    [InlineData("40001", "SerializationFailure")]
    [InlineData("40P01", "Deadlock")]
    [InlineData("55P03", "Timeout")]
    [InlineData("57014", "Timeout")]
    [InlineData("53300", "ConnectionFailure")]
    [InlineData("57P01", "ConnectionFailure")]
    [InlineData("57P02", "ConnectionFailure")]
    [InlineData("57P03", "ConnectionFailure")]
    [InlineData("42501", "PermissionDenied")]
    [InlineData("42P01", "SchemaMismatch")]
    [InlineData("42P07", "SchemaMismatch")]
    [InlineData("42703", "SchemaMismatch")]
    [InlineData("42710", "SchemaMismatch")]
    [InlineData("3F000", "SchemaMismatch")]
    [InlineData("22000", "Unknown")]
    public void SqlStateMapsToExpectedCategory(
        string sqlState,
        string expectedCategory)
    {
        AuthDatabaseErrorCategory expected = Enum.Parse<AuthDatabaseErrorCategory>(expectedCategory);
        Assert.Equal(expected, PostgresAuthDatabaseErrorClassifier.ClassifySqlState(sqlState));
    }
}
