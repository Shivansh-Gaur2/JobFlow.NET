# Interactive JobFlow UI design

## Goal

Provide a reusable, embedded UI for applications that use JobFlow. An
authorized person can find a job, inspect its real execution history, and
watch a plain-language animated scene that reflects the stored job state.

The UI is not a separate operations product and it does not decide who may
view a job. The host application owns identity, authorization, tenant rules,
and business names.

## User experience

The default detail page has three parts:

1. An animated scene with small people handling the job.
2. A short, plain-language explanation of the current state.
3. A factual timeline of attempts for investigation.

The scene is driven only by persisted JobFlow facts:

| Stored state | Scene meaning |
| --- | --- |
| `Pending` | The job is waiting at intake. |
| `InProgress` | A person has claimed the job and is working on it. |
| Failed attempt followed by `Pending` | The job is in recovery before another attempt. |
| `Completed` | The work has been delivered. |
| `Failed` | The case remains unfinished and shows the safe failure message. |
| `Abandoned` attempt | A previous worker lost ownership; the scene does not claim the business action did not happen. |

The UI never fabricates completion, progress, retries, or recovery. Generic
JobFlow jobs have no progress API in this slice, so the working animation
means only that the persisted state is `InProgress`.

## Architecture

```text
Host ASP.NET Core application
  -> authentication, authorization, tenant and business mapping
JobFlow.AspNetCore
  -> read-only endpoints, SignalR hub, registration and policy seam
JobFlow.Blazor
  -> reusable list/detail components and animated scene
JobFlow.SqlServer
  -> existing state/attempt store plus durable update outbox
JobFlow.Core
  -> scheduling contracts and source lifecycle types
```

`JobFlow.AspNetCore` calls a host-provided authorization service before
returning a job. An unauthorized direct job URL returns `404 Not Found` to
avoid exposing another job's existence. JobFlow does not infer ownership from
payload data. For customer-scoped lists, the host keeps its own indexed
business-record-to-`JobId` mapping.

The first release provides an internal operator job list. A host can use the
same detail component in its customer-facing pages after applying its own
resource authorization.

## Real-time updates

When SQL Server changes a job or attempt, JobFlow writes an outbox notification
in the same transaction. A background publisher sends that notification to a
SignalR hub after it commits. Messages are at-least-once hints, so clients
always reload the current job detail after a notification and when reconnecting.

The initial page load remains an HTTP read. While SignalR is unavailable, a
detail page polls an active job at a bounded interval and stops when it reaches
a terminal state. This preserves correctness during disconnects and supports
multiple application instances. A production deployment can use a SignalR
backplane or managed SignalR service according to its host's topology.

## Privacy and safety

- No anonymous access by default.
- No job payloads, raw exceptions, stack traces, connection strings, or secrets
  in the UI/API contract.
- Safe failure messages and error IDs are shown only after authorization.
- The first release is read-only: it does not retry, cancel, or mutate jobs.

## First delivery scope

- `JobFlow.AspNetCore` package with read-only job endpoints, authorization
  extension point, SignalR hub, and update publishing.
- `JobFlow.Blazor` Razor Class Library with a searchable list, detail page,
  animated mini-people scene, status copy, and attempt timeline.
- SQL migration and transactional outbox support.
- Sample ASP.NET Core application showing registration and authorization.
- Tests for authorization, endpoint contracts, scene state mapping, reconnect
  behavior, and transactional update records.

## Validation seams

Tests use these public seams:

1. The authorization service: unauthorized users cannot receive job details.
2. The read-only API: authorized callers receive safe job data only.
3. The update notifier: a committed job change creates one durable update
   record, and duplicate/out-of-order notifications cause a reload rather than
   a false state.
4. The scene model: each JobFlow state and attempt combination maps to the
   documented user-visible meaning.

## Not in this slice

- User-triggered retry or cancellation.
- Public tracking links.
- Handler-specific percentage progress.
- Raw payload inspection.
- A standalone dashboard application.
