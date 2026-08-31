namespace JobFlow.Core;

public interface IJobFailureClassifier
{
    JobFailure Classify(Exception exception, Guid errorId);
}
