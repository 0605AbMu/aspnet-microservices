namespace Microservices.Common.Jobs;

public interface IBackgroundJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
