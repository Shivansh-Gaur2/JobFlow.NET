# Operations and diagnostics

This guide explains what an operator can inspect when a job is delayed, retried, or failed.

## The two tables answer different questions

Think of `dbo.Jobs` as a whiteboard showing **where each job is now**. Think of `dbo.JobAttempts` as a logbook showing **how it got there**.

| Table | Question it answers | Example |
|---|---|---|
| `dbo.Jobs` | What is the job's current state? | `Pending`, `InProgress`, `Completed`, or `Failed` |
| `dbo.JobAttempts` | Which worker executions happened? | Worker A ran attempt 1; Worker B later recovered attempt 2 |

The dispatcher uses `dbo.Jobs` to decide what work is ready. It does not rebuild the current state by replaying history. `dbo.JobAttempts` exists for diagnosis and audit.

## When a job fails

When a handler throws, JobFlow does two separate things:

1. It writes the full exception to the application's configured `ILogger` provider.
2. It records an `ErrorId`, exception type, and a safe generic message on the matching `dbo.JobAttempts` row.

The SQL table deliberately does **not** receive the complete stack trace or arbitrary exception message. Those can contain secrets, customer data, connection strings, or SQL text.

Use the `ErrorId` to join the database row to application logs. For example, an operator can search their log system for the same `ErrorId` to find the full exception and stack trace.

If your application understands a known failure, it can replace the generic safe message without storing the raw exception. Register the classifier **before** JobFlow:

```csharp
builder.Services.AddSingleton<IJobFailureClassifier, PayrollFailureClassifier>();
builder.Services.UseSqlServerJobStore(connectionString);
```

For example, `PayrollFailureClassifier` can map a known HTTP 503 into `Payroll service is unavailable.` while the original exception stays in `ILogger`.

## Useful checks

Start with current jobs that need attention:

```sql
SELECT Id, JobType, Status, RetryCount, MaxRetries, NextRunAt, LockedBy, LeaseExpiresAt
FROM dbo.Jobs
WHERE Status IN (0, 1, 3) -- Pending, InProgress, Failed
ORDER BY NextRunAt, CreatedAt;
```

Then inspect one job's execution history:

```sql
SELECT AttemptNumber, WorkerId, Status, StartedAt, FinishedAt,
       ErrorId, FailureType, FailureMessage
FROM dbo.JobAttempts
WHERE JobId = @JobId
ORDER BY AttemptNumber;
```

`Abandoned` means a worker's lease expired before it recorded a result. It does not prove the business action did not happen. Handle external effects with a business idempotency key.

## Migrations in a deployment

`UseSqlServerJobStore` only registers services. It never changes a database automatically.

Run the explicit migration step before workers start:

```csharp
await host.Services.ApplyJobFlowSqlServerMigrationsAsync();
```

The migrator keeps its own history in `dbo.JobFlowSchemaMigrations` and uses a SQL Server application lock, so two deployment instances cannot apply the same version concurrently. Run it with a controlled deployment identity that can change schema; the normal worker should use a more restricted SQL login.

## What to alert on

- A growing count of `Failed` jobs.
- Jobs that remain `InProgress` beyond their expected execution time.
- A high number of `Abandoned` attempts, which can point to worker crashes, long GC pauses, or lease settings that are too aggressive.
- Repeated “could not claim” or “could not renew” errors in your application logs.

This package is still a preview. Before using it for critical workloads, test recovery, retries, and idempotent external effects in an environment that matches your deployment.
