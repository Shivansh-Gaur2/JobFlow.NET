# Release foundation design

## Goal

Make JobFlow.NET ready for deliberate preview releases. The repository should
have clear package identities, an auditable release path, useful public
metadata, and no publishing secret in source control.

The first release target is `0.1.0-preview.1`.

## Package identity

The current `JobFlow.Core` package ID is already owned by another NuGet
publisher. The projects will publish under this separate package family:

| Project | Package ID |
| --- | --- |
| `JobFlow.Core` | `JobFlow.NET.Core` |
| `JobFlow.SqlServer` | `JobFlow.NET.SqlServer` |
| `JobFlow.AspNetCore` | `JobFlow.NET.AspNetCore` |
| `JobFlow.Blazor` | `JobFlow.NET.Blazor` |

The C# namespaces remain `JobFlow.*`. Changing the package identity must not
change consumer source code.

## Repository surface

The root README will use the new package IDs and point to NuGet packages only
after they are published. Package metadata will include accurate descriptions,
tags, the repository URL, the MIT license, the packaged README, symbols, and
SourceLink. No generic logo or package icon will be added without an approved
brand asset.

The repository will add a release guide that explains version changes, package
validation, the `nuget` environment, the NuGet API-key secret, tags, and GitHub
release notes. Repository topics will be set directly on GitHub after the
release PR is ready: `dotnet`, `background-jobs`, `job-scheduler`,
`sql-server`, `signalr`, `blazor`, and `nuget`.

## Version and release model

`Directory.Build.props` remains the source of truth for the package version.
A version change is reviewed in a normal pull request with matching changelog
entries. The release workflow accepts an explicit version only as a safety
check and refuses to continue unless it matches the package version built from
the selected `main` commit.

The workflow is manual. Merging to `main` never publishes a package. The job
uses the protected GitHub environment named `nuget`, where the maintainer
stores `NUGET_API_KEY`.

Before publication, the workflow will:

1. Check out `main` and verify the supplied version matches all four packages.
2. Build and run the test suite.
3. Pack only the four library projects.
4. Check that `v<version>` does not already exist and that none of the exact
   package-version pairs already exists on NuGet.org.
5. Push the packages using the environment secret.
6. Create the annotated Git tag and GitHub Release with the matching changelog
   notes.

NuGet cannot make four package uploads atomic. If publishing fails after one
package succeeds, the workflow stops without creating the tag or GitHub
Release. The maintainer must resolve the failed upload and rerun only after
confirming the already-published package versions.

## CI and validation

Normal CI continues to restore, build, test, and pack, but it does not need
NuGet credentials. The package step targets only the library projects, so
non-packable samples do not produce package warnings.

Before a release, GitHub CI is the authority for the Docker-backed SQL Server
tests. The release workflow repeats the same build, test, and pack checks on a
GitHub runner before any upload.

## Scope boundaries

This work does not introduce automatic publishing on every merge, package
signing, a new visual brand, or a stable 1.0 claim. The first package is a
preview release and should state its known operational limitations.
