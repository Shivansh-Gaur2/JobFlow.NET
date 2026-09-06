# Run the workroom locally

The web sample is a development host for the Blazor components. It submits a
real SQL-backed job whose handler waits five seconds. That delay illustrates
execution; it does not generate a report, download, or other business result.

## Prerequisites

- .NET SDK 9 for the samples and test project.
- A reachable SQL Server instance and an existing database for this sample.
- Permission to create tables and indexes in that database when applying migrations.

Docker Desktop is one way to run SQL Server locally. Starting Docker alone
does not create a SQL Server container or database. Use a dedicated development
database, such as `JobFlowWebSample`, rather than pointing the sample at customer data.

## Configure and run

From the repository root, set the connection string in your PowerShell session.
For a local SQL Server instance using Windows authentication:

```powershell
$env:ConnectionStrings__JobFlow = 'Server=localhost;Database=JobFlowWebSample;Integrated Security=True;TrustServerCertificate=True;'
dotnet run --project samples/JobFlow.WebSample --urls http://localhost:5080
```

Alternatively, copy `samples/JobFlow.WebSample/appsettings.Development.json.example`
to `appsettings.Development.json` and change the value for your machine. That
local file is ignored by Git. For SQL authentication, keep credentials in local
configuration or a secret store. Do not commit them. `TrustServerCertificate=True`
is a local development setting, not a recommendation for a deployed service.

```powershell
Copy-Item samples/JobFlow.WebSample/appsettings.Development.json.example samples/JobFlow.WebSample/appsettings.Development.json
```

Open `http://localhost:5080` after the host reports that it is listening.
Click **Start a job**. The intended sequence is pending, running, then completed,
with the attempt recorded below the scene. A short pending state may finish
between refreshes; the screen reads current state rather than replaying every event.

## What the sample proves

The sample includes the Blazor document, interactive runtime, and component
stylesheet wiring. A successful local run proves that a browser can enqueue a
job and read its stored lifecycle from this host. It does not prove that the
same design is ready for a deployed customer portal.

The sample directly uses `IJobQuery` inside its server components and does not
demonstrate authentication or the protected HTTP endpoints. Keep it local.
The library endpoints have a separate host authorization contract, described
in [UI integration](ui-integration.md).

## Troubleshooting

- A database login error means the host cannot apply migrations. Check the
  instance, existing database, credentials, and schema permissions.
- A page that renders without responding to clicks usually means the browser
  did not load `_framework/blazor.web.js`; check the browser network tab.
- SQL integration tests use their own Testcontainers database. A failure to
  connect to `docker_engine` means those tests could not start their fixture.
- More details are in [troubleshooting](troubleshooting.md).
