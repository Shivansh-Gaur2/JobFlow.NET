using JobFlow.Core;

namespace JobFlow.Sample;

public class PrintJob : IJob
{
    public Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Running job with payload: {payload}");
        return Task.CompletedTask;
    }
}
