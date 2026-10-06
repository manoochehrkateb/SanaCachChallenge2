using Npgsql;

namespace SanaCash.GoldCredit.Persistence.Common;

public class PostgresSession
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    public bool IsActive => _connection is not null && _transaction is not null;
    public NpgsqlConnection Connection => _connection ?? throw new InvalidOperationException("No database transaction is active.");
    public NpgsqlTransaction Transaction => _transaction ?? throw new InvalidOperationException("No database transaction is active.");

    internal void Begin(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("A database transaction is already active in this scope.");
        }

        _connection = connection;
        _transaction = transaction;
    }

    internal void End()
    {
        _connection = null;
        _transaction = null;
    }
}
