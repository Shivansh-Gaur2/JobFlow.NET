namespace JobFlow.Core;

public interface IJobRetryPolicy
{
    JobRetryDecision Decide(JobRetryContext context);
}
