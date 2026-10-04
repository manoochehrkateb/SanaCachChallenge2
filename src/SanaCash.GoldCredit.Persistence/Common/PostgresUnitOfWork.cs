using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Data;

namespace SanaCash.GoldCredit.Persistence.Common;

public sealed class PostgresUnitOfWork(NpgsqlDataSource dataSource, PostgresSession session) : IUnitOfWork
{
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (session.IsActive)
        {
            return await operation(cancellationToken);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        session.Begin(connection, transaction);
        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            session.End();
        }
    }
}