namespace SanaCash.GoldCredit.Application.Abstractions.Data;

public interface IUnitOfWork
{
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}