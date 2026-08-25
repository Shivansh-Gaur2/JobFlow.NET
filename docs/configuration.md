# Configuration

Configure renewable leases when registering the SQL Server store:

```csharp
builder.Services.UseSqlServerJobStore(
    connectionString,
    options =>
    {
        options.LeaseDuration = TimeSpan.FromMinutes(5);
        options.RenewalInterval = TimeSpan.FromMinutes(1);
    });
```

## Defaults

| Setting | Default | Meaning |
|---|---:|---|
| `LeaseDuration` | 5 minutes | How long a claimed job remains owned if it is not renewed. |
| `RenewalInterval` | 1 minute | How often the dispatcher renews a running job's lease. |

## How to choose values

Start with the defaults unless you have a measured reason to change them.

For shorter jobs, lower values can recover a crashed worker sooner. Keep enough room for normal SQL delays and temporary pauses. For example, a 30-second lease should not renew every 29 seconds.

For longer jobs, choose a lease duration that comfortably covers a delayed renewal. Keep the renewal interval much shorter than the lease duration.

## Retries

The SQL store currently starts each job with `MaxRetries` set to three. When a handler throws, the dispatcher uses exponential backoff before rescheduling it. After the third recorded failure, the job is marked failed.

Retries are not a substitute for idempotency. A retry may happen after an external effect has already succeeded.
