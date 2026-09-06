# Build and verify NuGet packages

The four library projects are packable. Samples and tests are not. Shared
metadata lives in `Directory.Build.props`, including the preview version,
MIT license expression, repository URL, and symbol-package settings.

## Build a local feed

```powershell
dotnet restore JobFlow.sln
dotnet build JobFlow.sln --configuration Release --no-restore
dotnet test JobFlow.sln --configuration Release --no-build
dotnet pack src/JobFlow.Core/JobFlow.Core.csproj --configuration Release --no-build --output artifacts/packages
dotnet pack src/JobFlow.SqlServer/JobFlow.SqlServer.csproj --configuration Release --no-build --output artifacts/packages
dotnet pack src/JobFlow.AspNetCore/JobFlow.AspNetCore.csproj --configuration Release --no-build --output artifacts/packages
dotnet pack src/JobFlow.Blazor/JobFlow.Blazor.csproj --configuration Release --no-build --output artifacts/packages
```

The SQL tests require Docker. A build or pack success does not replace those
tests. CI runs restore, build, tests, and pack; it does not publish packages.

From a separate consumer project, install the exact version produced locally:

```powershell
dotnet add package JobFlow.SqlServer --version 0.1.0-preview.1 --source C:\path\to\JobFlow.NET\artifacts\packages
```

Use your actual feed path and package version. Restore the consumer with both
the local feed and NuGet.org configured, because dependencies come from NuGet.org.

## Before publishing

- Verify the chosen package IDs are owned by the maintainer on NuGet.org.
- Run the SQL integration suite and test the packages in a separate application.
- Inspect each archive for its README, assembly, dependencies, and version.
- Verify Blazor static assets in a consuming web host.
- Resolve the documented UI runtime, authorization, and notification limitations.
- Set release notes and a version that has not already been published.

Publishing is a separate maintainer action. Keep API keys in a secret store;
never put them in source, shell examples, or a committed NuGet configuration.
No public package availability is implied by these local-feed instructions.
