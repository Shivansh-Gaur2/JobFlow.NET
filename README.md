# JobFlow.NET

[![Continuous integration](https://github.com/Shivansh-Gaur2/JobFlow.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/Shivansh-Gaur2/JobFlow.NET/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

JobFlow.NET is a pre-release .NET library for one-time and delayed background jobs backed by SQL Server.

> **Status: pre-release and under active development.** JobFlow uses renewable SQL Server job leases and has automated SQL integration coverage. It still provides at-least-once delivery: job handlers must be safe to run more than once. Do not use it in production yet.

## Why JobFlow.NET?

The project explores a small, explicit job-scheduling model:

- A job implements `IJob` and receives an optional payload plus a cancellation token.
- SQL Server stores jobs and atomically claims ready work for competing workers.
- A hosted dispatcher resolves a fresh job instance from dependency injection for each execution.
- Failed jobs use a configurable retry policy; permanent configuration failures stop immediately.
- Each claim has a lease token. Only the worker holding the current, unexpired token can complete, fail, or renew its job.

The SQL Server store uses `UPDLOCK` and `READPAST` to make claiming a job a single database operation. An expired lease can be reclaimed by another worker. This protects the queue from a crashed worker, but it also means an external side effect (for example, charging a card) must use its own idempotency key.

## Architecture at a glance

The small diagram below shows the important split: `dbo.Jobs` holds the current answer, while `dbo.JobAttempts` records each worker execution.

[Open the interactive architecture diagram](docs/diagrams/jobflow-overview.html)

```mermaid
flowchart LR
    App[Your application] --> Scheduler[JobScheduler]
    Scheduler --> Store[SqlJobStore]
    Dispatcher[JobDispatcher] -->|claim, renew, finish| Store
    Dispatcher --> Handler[IJob handler]
    Store --> Jobs[(dbo.Jobs: current state)]
    Store --> Attempts[(dbo.JobAttempts: execution history)]
```

## Packages and installation

| Project | Purpose | Target |
| --- | --- | --- |
| `JobFlow.NET.Core` | Scheduling, execution, retries, and query contracts | .NET 8 |
| `JobFlow.NET.SqlServer` | SQL storage, migrations, leases, and update outbox | .NET 8 |
| `JobFlow.NET.AspNetCore` | Authorized query endpoints and SignalR notifications | .NET 8 |
| `JobFlow.NET.Blazor` | Job list, attempt history, and animated work scene | .NET 8 |

Build from source or use the [local NuGet feed instructions](docs/packaging.md).
The repository's package version is a preview; a successful local pack does
not mean that version has been published on NuGet.org.

## Try it locally

```powershell
git clone https://github.com/Shivansh-Gaur2/JobFlow.NET.git
cd JobFlow.NET
dotnet restore JobFlow.sln
dotnet build JobFlow.sln --configuration Release --no-restore
```

The SQL provider needs an existing SQL Server database. Migrations create
JobFlow's tables, not the database itself. Follow the [local workroom guide](docs/local-workroom.md)
to configure a database and start the web sample on `http://localhost:5080`.

The sample submits a five-second background job and reads its stored status
every half second. It shows the work scene and attempt history. It is an
unfinished development sample: browser startup and the full interaction still
need verification. There is no hosted demo linked from this repository.

## What works today

- Immediate and delayed jobs with SQL persistence.
- Renewable worker leases and recovery after an expired lease.
- Replaceable global retry policy with backoff and jitter.
- Job queries, cursor pagination, attempt history, and safe failure details.
- Authorized HTTP queries and a SignalR notification publisher.
- Blazor components for displaying supplied job records.

The web sample uses polling and does not connect to the custom SignalR hub.
The UI does not provide percentage progress, cancellation, or manual retries.
Read the [UI integration guide](docs/ui-integration.md) before exposing it to users.

## Quick start

Register the SQL Server store and your job type when configuring the host:

```csharp
using JobFlow.Core;
using JobFlow.SqlServer;
using Microsoft.Extensions.DependencyInjection;

builder.Services.UseSqlServerJobStore(
    "Server=localhost,1433;Database=JobFlow;User Id=sa;Password=your-password;TrustServerCertificate=True;");

builder.Services.AddTransient<PrintJob>();
```

Configure the global retry policy when needed. `MaxAttempts` includes the first
execution, so a value of three permits at most three total executions:

```csharp
builder.Services.UseSqlServerJobStore(
    connectionString,
    configureRetryOptions: retry =>
    {
        retry.MaxAttempts = 3;
        retry.BaseDelay = TimeSpan.FromSeconds(2);
        retry.MaxDelay = TimeSpan.FromMinutes(5);
    });
```

Apply the JobFlow SQL migrations before starting workers:

```csharp
var host = builder.Build();

await host.Services.ApplyJobFlowSqlServerMigrationsAsync();
```

Schedule work through `JobScheduler`:

```csharp
var scheduler = host.Services.GetRequiredService<JobScheduler>();

await scheduler.EnqueueAsync<PrintJob>("hello");
await scheduler.ScheduleAsync<PrintJob>(TimeSpan.FromMinutes(5), "run later");
```

`PrintJob` implements `IJob`:

```csharp
public sealed class PrintJob : IJob
{
    public Task ExecuteAsync(string? payload, CancellationToken ct)
    {
        Console.WriteLine(payload);
        return Task.CompletedTask;
    }
}
```

`UseSqlServerJobStore` only registers JobFlow services. Applying migrations is explicit, so production deployments can upgrade the database before workers begin processing jobs.

## Guides

For the details behind the quick start, read the [documentation index](https://github.com/Shivansh-Gaur2/JobFlow.NET/tree/main/docs):

- [Getting started](https://github.com/Shivansh-Gaur2/JobFlow.NET/blob/main/docs/getting-started.md)
- [Delivery and renewable leases](https://github.com/Shivansh-Gaur2/JobFlow.NET/blob/main/docs/delivery-and-leases.md)
- [Idempotent job handlers](https://github.com/Shivansh-Gaur2/JobFlow.NET/blob/main/docs/idempotent-job-handlers.md)
- [Configuration](https://github.com/Shivansh-Gaur2/JobFlow.NET/blob/main/docs/configuration.md)
- [Operations and diagnostics](https://github.com/Shivansh-Gaur2/JobFlow.NET/blob/main/docs/operations-and-diagnostics.md)
- [Troubleshooting](https://github.com/Shivansh-Gaur2/JobFlow.NET/blob/main/docs/troubleshooting.md)
- [Releasing](https://github.com/Shivansh-Gaur2/JobFlow.NET/blob/main/docs/releasing.md)

## Repository layout

```text
src/
  JobFlow.Core/       Contracts, scheduling, dispatching, lifecycle, and failure types
  JobFlow.SqlServer/  Registration, SQL store, and embedded versioned migrations
tests/
  JobFlow.SqlServer.Tests/  Real SQL Server integration tests using Testcontainers
samples/
  JobFlow.Sample/     Small host registration and scheduling example
```

## Current requirements

- .NET SDK 9.0 or later to build the full solution.
- SQL Server 2022 or later for the SQL Server provider.
- Docker Desktop to run the SQL Server integration tests. The tests start their own disposable SQL Server container.

## Build locally

```powershell
dotnet restore JobFlow.sln
dotnet build JobFlow.sln --configuration Release --no-restore
dotnet test JobFlow.sln --configuration Release --no-build
```

The test suite exercises real SQL Server behavior through Testcontainers. The sample application in `samples/JobFlow.Sample` shows basic registration and scheduling.

## Roadmap

1. Verify the complete browser experience and add API/UI regression coverage.
2. Connect the UI to SignalR with reconnect recovery and polling fallback.
3. Define outbox retention and validate notifications across multiple web hosts.
4. Validate packages in a separate consumer before publishing a preview.
5. Explore recurring jobs and additional storage providers after the SQL path is proven.

See the [release preparation guide](docs/packaging.md) for package checks and
the [change log](CHANGELOG.md) for the current unreleased scope.

## Contributing and security

Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. See [SECURITY.md](SECURITY.md) for private vulnerability reporting guidance and [SUPPORT.md](SUPPORT.md) for questions.

## License

Licensed under the [MIT License](LICENSE).
