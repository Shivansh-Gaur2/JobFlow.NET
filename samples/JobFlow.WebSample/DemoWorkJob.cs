using JobFlow.Core;

namespace JobFlow.WebSample;

public sealed class DemoWorkJob : IJob
{
    public async Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
}
