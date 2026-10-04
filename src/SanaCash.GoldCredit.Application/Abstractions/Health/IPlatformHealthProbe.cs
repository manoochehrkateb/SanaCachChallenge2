namespace SanaCash.GoldCredit.Application.Abstractions.Health;

public interface IPlatformHealthProbe
{
    Task<PlatformHealth> CheckAsync(CancellationToken cancellationToken = default);
}