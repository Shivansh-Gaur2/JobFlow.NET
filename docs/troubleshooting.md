# Troubleshooting

## The `dbo.Jobs` table is not created

`UseSqlServerJobStore` runs the embedded schema script when the application starts.

Check that:

- the connection string points to the intended database;
- the SQL login can create and alter `dbo.Jobs`;
- the application can reach SQL Server over the configured network and port.

For production later, prefer a controlled database-migration process instead of granting broad schema permissions to the running application.

## Integration tests cannot connect to Docker

The SQL integration tests use Testcontainers, which starts a disposable SQL Server container.

Check that Docker Desktop is running, then run:

```powershell
docker ps
dotnet test JobFlow.sln --configuration Release --no-build
```

If Docker reports an access error, restart Docker Desktop and make sure your Windows account can use the Docker engine.

## A job stays in progress

Check the worker process first. A running worker renews its lease, so another worker should not take the job while that worker is healthy.

If the worker crashed, wait until the lease expires. Another worker can then reclaim the job. The default lease duration is five minutes.

## A job ran twice

This can happen after a worker performs an external action but fails before recording completion. This is expected under at-least-once delivery.

Use a stable business idempotency key. See [idempotent job handlers](idempotent-job-handlers.md).

## A handler type cannot be resolved

Register the job type in dependency injection:

```csharp
builder.Services.AddTransient<YourJob>();
```

The job must implement `IJob`, and the registered type must match the type you enqueue.
