using Microsoft.EntityFrameworkCore;
using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Data;

namespace SanaCash.GoldCredit.Persistence.Common;

public class GenericRepository<TEntity>(GoldCreditDbContext context, PostgresSession session) : IGenericRepository<TEntity>
    where TEntity : class
{
    protected GoldCreditDbContext Context { get; } = context;
    protected DbSet<TEntity> Entities => Context.Set<TEntity>();
    private readonly PostgresSession _session = session;

    public IQueryable<TEntity> Query() => Entities;

    public ValueTask<TEntity?> GetByIdAsync(object[] keyValues, CancellationToken cancellationToken = default) =>
        Entities.FindAsync(keyValues, cancellationToken);

    public Task AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        Entities.AddAsync(entity, cancellationToken).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Context.SaveChangesAsync(cancellationToken);

    public void Update(TEntity entity) => Entities.Update(entity);

    public void Remove(TEntity entity) => Entities.Remove(entity);

    protected async Task<int> InsertAsync(
        string sql,
        Action<NpgsqlParameterCollection> configureParameters,
        CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(sql, _session.Connection, _session.Transaction);
        configureParameters(command.Parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
