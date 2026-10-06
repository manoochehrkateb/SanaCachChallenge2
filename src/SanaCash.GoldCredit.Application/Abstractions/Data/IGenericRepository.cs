namespace SanaCash.GoldCredit.Application.Abstractions.Data;

public interface IGenericRepository<TEntity>
    where TEntity : class
{
    IQueryable<TEntity> Query();
    ValueTask<TEntity?> GetByIdAsync(object[] keyValues, CancellationToken cancellationToken = default);
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
}
