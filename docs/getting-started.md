# Getting started

This guide creates one worker process that stores jobs in SQL Server and executes them in the background.

## 1. Install the SQL Server package

Once the preview package is published, add the SQL Server package to your application:

```powershell
dotnet add package JobFlow.SqlServer --prerelease
```

`JobFlow.SqlServer` brings in `JobFlow.Core`, so you do not need to install both packages manually.

## 2. Create a job

A job implements `IJob`. Keep the handler focused on one piece of work.

```csharp
using JobFlow.Core;

public sealed class SendWelcomeEmailJob : IJob
{
    public Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        // Read the payload, send the email, and respect ct.
        return Task.CompletedTask;
    }
}
```

The `payload` is a string. For structured data, serialize a small JSON object and validate it inside the job.

## 3. Register JobFlow.NET

Register the SQL Server store and every job type in your host setup:

```csharp
using JobFlow.SqlServer;

var connectionString = builder.Configuration.GetConnectionString("JobFlow")
    ?? throw new InvalidOperationException("Connection string 'JobFlow' was not found.");

builder.Services.UseSqlServerJobStore(connectionString);
builder.Services.AddTransient<SendWelcomeEmailJob>();
```

Build the host and apply migrations before starting the worker:

```csharp
var host = builder.Build();

await host.Services.ApplyJobFlowSqlServerMigrationsAsync();
```

`UseSqlServerJobStore` registers the scheduler and background dispatcher. It does not change the database by itself. Apply migrations as a controlled deployment step, using a SQL login that has schema-change permission. The running worker can use a more restricted SQL login after that step.

## 4. Enqueue a job

Resolve `JobScheduler` from dependency injection and enqueue work:

```csharp
using JobFlow.Core;
using Microsoft.Extensions.DependencyInjection;

var scheduler = host.Services.GetRequiredService<JobScheduler>();

await scheduler.EnqueueAsync<SendWelcomeEmailJob>("{\"userId\":\"123\"}");
await scheduler.ScheduleAsync<SendWelcomeEmailJob>(
    TimeSpan.FromMinutes(10),
    "{\"userId\":\"456\"}");
```

`EnqueueAsync` makes the job ready immediately. `ScheduleAsync` makes it ready after the supplied delay.

## 5. Run the host

Start the host normally:

```csharp
await host.RunAsync();
```

The dispatcher polls SQL Server for a ready job, claims it, then runs the matching job type from dependency injection.

## Before you add real side effects

Read [delivery and renewable leases](delivery-and-leases.md) and [idempotent job handlers](idempotent-job-handlers.md). A background scheduler must be allowed to deliver a job more than once when a worker fails at the wrong moment.
