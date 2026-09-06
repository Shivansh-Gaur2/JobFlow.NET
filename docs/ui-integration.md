# UI integration

JobFlow exposes reusable display components and a separate ASP.NET Core API.
The host application supplies authentication and business ownership rules.

## Display components

`JobListView` accepts an `IReadOnlyList<JobSummary>` and a `DetailRoutePrefix`.
`JobDetailsView` accepts a `JobDetails` record and renders the scene, metadata,
and attempts. Neither component fetches data or checks permissions. Authorize
and load the records before passing them to these components.

```razor
@using JobFlow.Blazor.Components

<JobDetailsView Job="authorizedJob" />
```

Include the host's generated CSS isolation bundle so the component styles load.
The work scene supports the browser's reduced-motion preference. Its animation
is illustrative: it is not evidence of handler progress or a worker heartbeat.

## HTTP endpoints

Register `AddJobFlowWeb()` after configuring the host's authentication,
authorization, and SQL store. Map `MapJobFlow()` in the host's endpoint setup.

| Route | Purpose |
| --- | --- |
| `GET /jobflow/jobs` | Search by status, job type, worker, page size, and cursor |
| `GET /jobflow/jobs/{jobId}` | Read one job and its scene interpretation |
| `/jobflow/updates` | SignalR hub |

`IJobViewerAuthorization.CanViewAsync` controls access to one job.
`IJobListAuthorization.CanSearchAsync` controls enumeration. Both default to
deny. An authenticated caller denied by these contracts receives HTTP 404.
Authentication challenges are handled by the host's authentication scheme.

List permission currently grants access to the operational search results; it
does not filter each row by customer or call the per-job policy. Do not grant
it to a tenant-scoped user unless the whole underlying dataset is in their scope.
Customer-filtered lists require a host-specific query integration.

Responses omit payloads and raw exceptions. Safe failure messages still need
careful classification by the host. Worker IDs and job type names are operational
data and may not be suitable for every customer-facing page.

## Notifications

The SQL store records an outbox entry in the same transaction as enqueue,
claim, completion, or failure. The publisher sends `jobChanged` with a job ID.
Clients subscribe using `WatchJob(jobId)` after authentication and authorization.

A notification should trigger an authorized reload. Fetch on initial load and
after reconnecting, and subscribe again when the connection changes. Duplicate
notifications must not create duplicate timeline entries.

The current sample has no client for this hub. Group membership is authorized
when joining; ongoing permission revocation and notification delivery across
multiple hosts need additional design and tests. Published outbox rows are
retained indefinitely today. These are release limitations, not guarantees
provided by the current implementation.
