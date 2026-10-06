using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Data;

namespace SanaCash.GoldCredit.Persistence.Common;

public class PostgresUnitOfWork : IUnitOfWork
{
    private readonly GoldCreditDbContext _dbContext;
    private readonly PostgresSession _session;

    public PostgresUnitOfWork(GoldCreditDbContext dbContext, PostgresSession session)
    {
        _dbContext = dbContext;
        _session = session;
    }

    public PostgresUnitOfWork(NpgsqlDataSource dataSource, PostgresSession session)
        : this(
            new GoldCreditDbContext(
                new DbContextOptionsBuilder<GoldCreditDbContext>()
                    .UseNpgsql(dataSource)
                    .Options),
            session)
    {
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (_session.IsActive)
        {
            return await operation(cancellationToken);
        }

        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var dbTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        _session.Begin(connection, dbTransaction);
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
            _session.End();
            await _dbContext.Database.CloseConnectionAsync();
        }
    }
}
