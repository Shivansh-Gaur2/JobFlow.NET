using JobFlow.Core;

namespace JobFlow.SqlServer.Tests;

public sealed class RetryPolicyTests
{
    [Fact]
    public void Decide_doubles_the_delay_and_never_exceeds_the_configured_cap()
    {
        var policy = new ExponentialBackoffRetryPolicy(new JobRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(3),
            JitterFactor = 0
        });
        var failure = new JobFailure(Guid.NewGuid(), "TimeoutException", "Dependency timed out.");

        var first = policy.Decide(new JobRetryContext(Guid.NewGuid(), "EmailJob", 1, 3, failure));
        var second = policy.Decide(new JobRetryContext(Guid.NewGuid(), "EmailJob", 2, 3, failure));

        Assert.Equal(TimeSpan.FromSeconds(2), first.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(3), second.RetryDelay);
    }

    [Fact]
    public void Decide_stops_on_the_final_allowed_attempt()
    {
        var policy = new ExponentialBackoffRetryPolicy(new JobRetryOptions { JitterFactor = 0 });
        var failure = new JobFailure(Guid.NewGuid(), "TimeoutException", "Dependency timed out.");

        var decision = policy.Decide(new JobRetryContext(Guid.NewGuid(), "EmailJob", 3, 3, failure));

        Assert.True(decision.IsTerminal);
    }

    [Fact]
    public void Classify_marks_invalid_job_configuration_as_non_retryable()
    {
        var classifier = new DefaultJobFailureClassifier();

        var failure = classifier.Classify(
            new InvalidDataException("Unknown job type 'TypoJob'."),
            Guid.NewGuid());

        Assert.Equal(JobFailureDisposition.NonRetryable, failure.Disposition);
    }

    [Fact]
    public void Decide_stops_a_non_retryable_failure_before_the_attempt_limit()
    {
        var policy = new ExponentialBackoffRetryPolicy(new JobRetryOptions
        {
            MaxAttempts = 3,
            JitterFactor = 0
        });
        var failure = new JobFailure(
            Guid.NewGuid(),
            "InvalidDataException",
            "Job configuration is invalid.",
            JobFailureDisposition.NonRetryable);

        var decision = policy.Decide(new JobRetryContext(
            Guid.NewGuid(),
            "EmailJob",
            attemptNumber: 1,
            maxAttempts: 3,
            failure));

        Assert.True(decision.IsTerminal);
        Assert.Null(decision.RetryDelay);
    }
}
