namespace JobFlow.Core;

public sealed class DefaultJobFailureClassifier : IJobFailureClassifier
{
    public JobFailure Classify(Exception exception, Guid errorId)
    {
        return new JobFailure(
            errorId,
            exception.GetType().Name,
            "Job execution failed. See ErrorId for details.",
            exception is InvalidDataException
                ? JobFailureDisposition.NonRetryable
                : JobFailureDisposition.Retryable);
    }
}
