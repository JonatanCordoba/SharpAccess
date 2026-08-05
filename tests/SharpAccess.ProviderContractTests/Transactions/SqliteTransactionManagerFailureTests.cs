using SharpAccess.Sqlite;
using System.Data;
using System.Data.Common;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]

public sealed class SqliteTransactionManagerFailureTests
{
    [Fact]
    public async Task TransactionManagerPreservesOriginalFailureWhenRollbackFails()
    {
        SqliteAuthTransactionManager manager = new();
        RollbackFailureConnection connection = new();

        RollbackOriginalException exception = await Assert.ThrowsAsync<RollbackOriginalException>(() => manager.ExecuteAsync<int>(
            connection,
            IsolationLevel.Serializable,
            static (_, _) => Task.FromException<int>(new RollbackOriginalException())));

        Assert.Equal("original", exception.Message);
        Assert.True(connection.Transaction.RollbackAttempted);
    }

    private sealed class RollbackOriginalException : Exception
    {
        public RollbackOriginalException()
            : base("original")
        {
        }
    }

    private sealed class RollbackFailureConnection : DbConnection
    {
        public RollbackFailureTransaction Transaction { get; } = new();

#pragma warning disable CS8765
        public override string ConnectionString { get; set; } = string.Empty;
#pragma warning restore CS8765

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => Transaction;

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class RollbackFailureTransaction : DbTransaction
    {
        public bool RollbackAttempted { get; private set; }

        public override IsolationLevel IsolationLevel => IsolationLevel.Serializable;

        protected override DbConnection? DbConnection => null;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
            RollbackAttempted = true;
            throw new InvalidOperationException("rollback failed");
        }
    }
}
