namespace JobFlow.Core;

public enum JobAttemptStatus
{
    Running,
    Completed,
    Failed,
    Abandoned
}