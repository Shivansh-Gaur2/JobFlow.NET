namespace JobFlow.Core;

/// <summary>Durable notifications emitted only after JobFlow state changes commit.</summary>
public interface IJobUpdateOutbox
{
    Task<IReadOnlyList<JobUpdate>> GetUnpublishedAsync(int take, CancellationToken ct);
    Task MarkPublishedAsync(Guid updateId, CancellationToken ct);
}
