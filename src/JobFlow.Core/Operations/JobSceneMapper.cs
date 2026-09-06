namespace JobFlow.Core;

public static class JobSceneMapper
{
    public static JobScene Map(JobDetails job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var latest = job.Attempts.LastOrDefault();
        var attemptNumber = latest?.AttemptNumber ?? 0;

        return job.Status switch
        {
            JobStatus.Pending when latest?.Status == JobAttemptStatus.Failed => new(
                JobSceneState.Recovering,
                $"The previous attempt needs recovery. The job is waiting for attempt {attemptNumber + 1} of {job.MaxAttempts}.",
                attemptNumber,
                job.MaxAttempts),
            JobStatus.Pending => new(
                JobSceneState.WaitingAtIntake,
                "The job is waiting for a worker.",
                attemptNumber,
                job.MaxAttempts),
            JobStatus.InProgress => new(
                JobSceneState.BeingWorked,
                $"A worker is processing attempt {attemptNumber} of {job.MaxAttempts}.",
                attemptNumber,
                job.MaxAttempts),
            JobStatus.Completed => new(
                JobSceneState.Delivered,
                "The job completed successfully.",
                attemptNumber,
                job.MaxAttempts),
            JobStatus.Failed => new(
                JobSceneState.Unfinished,
                latest?.FailureMessage ?? "The job could not be completed.",
                attemptNumber,
                job.MaxAttempts),
            _ => new(JobSceneState.UnderInvestigation, "The job state could not be interpreted.", attemptNumber, job.MaxAttempts)
        };
    }
}
