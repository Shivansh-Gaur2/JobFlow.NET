# JobFlow.NET

JobFlow.NET is an experimental .NET library for one-time and delayed background jobs backed by SQL Server.

> **Status: pre-release and under active development.** The current distributed-job claim mechanism and failure handling have not yet been proven by automated or multi-instance integration tests. Do not use this repository in production.

## Why JobFlow.NET?

The project explores a small, explicit job-scheduling model:

- A job implements `IJob` and receives an optional payload plus a cancellation token.
- SQL Server stores jobs and atomically claims ready work for competing workers.
- A hosted dispatcher resolves a fresh job instance from dependency injection for each execution.
- Failed jobs are retried with exponential backoff until their retry limit is reached.

The initial SQL Server store uses `UPDLOCK` and `READPAST` to make claiming a job a single database operation. The intended outcome is that multiple instances can share a queue without executing the same claimed job simultaneously; that property still needs live proof before any release.

## Repository layout

```text
src/
  JobFlow.Core/       Job contracts, records, and dispatcher
  JobFlow.SqlServer/  SQL Server-backed job store and schema script
```

## Current requirements

- .NET SDK 9.0 or later to build the full solution (the SQL Server project currently targets `net9.0`).
- SQL Server for eventual end-to-end execution testing.

## Build locally

```powershell
dotnet restore JobFlow.sln
dotnet build JobFlow.sln --configuration Release --no-restore
dotnet test JobFlow.sln --configuration Release --no-build
```

There is not yet a sample application or test project. The final command is included so the standard verification path is ready when tests are added.

## Roadmap

1. Finish registration and the typed scheduling convenience module.
2. Add a sample worker and automated integration tests.
3. Prove competing-consumer behaviour with multiple worker instances.
4. Add recurring jobs using cron expressions.
5. Consider additional storage adapters only after the SQL Server behaviour is proven.

## Contributing and security

Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. See [SECURITY.md](SECURITY.md) for private vulnerability reporting guidance and [SUPPORT.md](SUPPORT.md) for questions.

## License

Licensed under the [MIT License](LICENSE).
