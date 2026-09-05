namespace JobFlow.Core;

public enum JobFailureDisposition
{
    Retryable,
    NonRetryable
}

public sealed record JobFailure
{
    public JobFailure(
        Guid errorId,
        string failureType,
        string safeMessage,
        JobFailureDisposition disposition = JobFailureDisposition.Retryable)
    {
        if (errorId == Guid.Empty)
        {
            throw new ArgumentException("Error ID must not be empty.", nameof(errorId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureType);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);

        if (failureType.Length > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureType),
                "Failure type must be 200 characters or fewer.");
        }

        if (safeMessage.Length > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(safeMessage),
                "Safe failure message must be 1000 characters or fewer.");
        }

        ErrorId = errorId;
        FailureType = failureType;
        SafeMessage = safeMessage;
        Disposition = disposition;
    }

    public Guid ErrorId { get; }

    public string FailureType { get; }

    public string SafeMessage { get; }

    public JobFailureDisposition Disposition { get; }
}
