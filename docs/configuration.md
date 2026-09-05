# Configuration

Configure renewable leases when registering the SQL Server store:

```csharp
builder.Services.UseSqlServerJobStore(
    connectionString,
    configureLeaseOptions: lease =>
    {
        lease.LeaseDuration = TimeSpan.FromMinutes(5);
        lease.RenewalInterval = TimeSpan.FromMinutes(1);
    },
    configureRetryOptions: retry =>
    {
        retry.MaxAttempts = 3;
        retry.BaseDelay = TimeSpan.FromSeconds(2);
        retry.MaxDelay = TimeSpan.FromMinutes(5);
        retry.JitterFactor = 0.20;
    });
```

## Defaults

| Setting | Default | Meaning |
|---|---:|---|
| `LeaseDuration` | 5 minutes | How long a claimed job remains owned if it is not renewed. |
| `RenewalInterval` | 1 minute | How often the dispatcher renews a running job's lease. |
| `MaxAttempts` | 3 | Maximum total executions for a newly queued job. |
| `BaseDelay` | 2 seconds | Delay before the first retry. |
| `MaxDelay` | 5 minutes | Upper limit for one retry delay. |
| `JitterFactor` | 0.20 | Random delay variation to avoid retry storms. |

## How to choose values

Start with the defaults unless you have a measured reason to change them.

For shorter jobs, lower values can recover a crashed worker sooner. Keep enough room for normal SQL delays and temporary pauses. For example, a 30-second lease should not renew every 29 seconds.

For longer jobs, choose a lease duration that comfortably covers a delayed renewal. Keep the renewal interval much shorter than the lease duration.

JobFlow validates this at startup: both values must be positive, and `RenewalInterval` must be shorter than `LeaseDuration`. Otherwise, a worker could miss its first renewal and let another worker recover the same job while the first worker is still busy.

## Retries

`MaxAttempts` includes the first execution. With the default value of three,
attempts one and two may be retried; attempt three is terminal. Retryable
failures use capped exponential backoff with jitter. A known invalid job
configuration is terminal immediately, because retrying it cannot fix it.

A host can replace the global policy before registering JobFlow:

```csharp
builder.Services.AddSingleton<IJobRetryPolicy, PayrollRetryPolicy>();
builder.Services.UseSqlServerJobStore(connectionString);
```

Retries are not a substitute for idempotency. A retry may happen after an external effect has already succeeded.
