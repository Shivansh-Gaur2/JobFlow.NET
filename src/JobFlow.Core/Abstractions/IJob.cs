namespace JobFlow.Core;

public interface IJob
{
    Task ExecuteAsync(string? payload, CancellationToken ct);
}
