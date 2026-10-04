using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Domain.Shared.Primitives;

namespace SanaCash.GoldCredit.Application.Abstractions.Data;

public interface IIdempotencyStore
{
    Task<Result<TResponse>> ExecuteAsync<TResponse>(
        ClientId clientId,
        string operation,
        string key,
        string requestFingerprint,
        Func<CancellationToken, Task<Result<TResponse>>> execute,
        CancellationToken cancellationToken = default);
}