# Releasing JobFlow.NET

JobFlow.NET releases are manual and run from `main`. Normal CI never publishes
to NuGet.org.

## Prepare a release

1. Update `VersionPrefix` and `VersionSuffix` in `Directory.Build.props`.
2. Add the release section to `CHANGELOG.md` using the exact package version.
3. Open and merge a release-preparation pull request after its checks pass.
4. Confirm all four package IDs and the version are absent from NuGet.org.

The package IDs are `JobFlow.NET.Core`, `JobFlow.NET.SqlServer`,
`JobFlow.NET.AspNetCore`, and `JobFlow.NET.Blazor`. Package names are separate
from the `JobFlow.*` C# namespaces.

## Configure publishing

Create a GitHub environment named `nuget`. Add protection rules appropriate
for the maintainers, then add `NUGET_API_KEY` as an environment secret. The key
must allow pushing each JobFlow.NET package ID. Do not add the key to a local
NuGet configuration, repository secret file, issue, pull request, or shell
history.

## Publish

Open the **Release JobFlow.NET packages** workflow in GitHub Actions and start
it with the exact version from `Directory.Build.props`, for example
`0.1.0-preview.1`. The workflow checks the version, builds, runs tests, packs
the four libraries, checks NuGet.org, pushes packages and symbols, creates the
annotated `v<version>` tag, and creates a GitHub Release from the matching
changelog section.

If the workflow stops after some packages were uploaded, do not create a tag
manually. Investigate the failed upload first. The workflow has a recovery mode
for a confirmed partial upload; it skips packages that already exist and only
creates the tag and GitHub Release after every package is available.

## Verify

After the workflow completes, install the SQL Server package in a clean sample:

```powershell
dotnet new console --output JobFlowReleaseSmoke
Set-Location JobFlowReleaseSmoke
dotnet add package JobFlow.NET.SqlServer --version 0.1.0-preview.1 --prerelease
```

The package download proves availability, not production readiness. Read the
delivery, idempotency, and UI integration guides before using JobFlow in a
system with external side effects.
