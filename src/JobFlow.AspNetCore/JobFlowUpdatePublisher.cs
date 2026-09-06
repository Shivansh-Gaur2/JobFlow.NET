using JobFlow.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobFlow.AspNetCore;

internal sealed class JobFlowUpdatePublisher : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly IJobUpdateOutbox _outbox;
    private readonly IHubContext<JobFlowHub> _hub;
    private readonly ILogger<JobFlowUpdatePublisher> _logger;

    public JobFlowUpdatePublisher(
        IJobUpdateOutbox outbox,
        IHubContext<JobFlowHub> hub,
        ILogger<JobFlowUpdatePublisher> logger)
    {
        _outbox = outbox;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var update in await _outbox.GetUnpublishedAsync(100, stoppingToken))
                {
                    await _hub.Clients.Group(JobFlowGroups.ForJob(update.JobId))
                        .SendAsync("jobChanged", update.JobId, stoppingToken);
                    await _outbox.MarkPublishedAsync(update.Id, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not publish JobFlow job updates.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
