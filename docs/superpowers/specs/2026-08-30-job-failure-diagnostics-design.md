# Job failure diagnostics design

## Goal

Make a failed JobFlow execution understandable to an operator without storing
raw job payloads or full exception details in the JobFlow database.

## Problem

`Jobs` records the current scheduler state and `JobAttempts` records an
execution outcome. A failed attempt currently has no safe explanation of why
it failed. Operators cannot distinguish an external-service outage from an
application problem without access to separate logs.

## Design

When a job handler throws, `JobDispatcher` creates an `ErrorId` and writes the
full exception through standard .NET `ILogger` logging. It supplies a
`JobFailure` value to `IJobStore.MarkFailedAsync`.

`JobFailure` contains only:

- `ErrorId`: correlates the database record with application logs.
- `FailureType`: a short technical category such as `HttpRequestException`.
- `SafeMessage`: a short operator-facing explanation.

The store updates the current `Jobs` row and the matching `Running`
`JobAttempts` row in one SQL transaction. The attempt receives `Failed`, a
finish timestamp, and the failure data. A retry and a terminal failure both
record the same attempt diagnosis.

## Safe message policy

The default classifier must never copy an exception message into `SafeMessage`.
Its value is: `Job execution failed. See ErrorId for details.`

An optional host-provided `IJobFailureClassifier` can map known exceptions to
safe business messages. For example, a Payroll application can map an HTTP
503 response to `Payroll service is unavailable.` The full exception remains
only in the host application's logging destination.

## Schema and compatibility

`JobAttempts` gains nullable `FailureType`, `FailureMessage`, and `ErrorId`
columns. The schema script must create these columns for new databases and add
each missing column for existing databases. Existing history remains valid:
completed and abandoned attempts, and attempts created before this feature,
have null failure fields.

## Boundaries

This slice does not add a dashboard, `IJobQuery` implementation, raw payload
access, or `JobEvents`. Those are follow-up modules. The query module will
later read these safe fields when rendering an operations screen.

## Validation

- A retrying failure records the safe diagnostics on its attempt.
- A terminal failure records the safe diagnostics on its attempt.
- A stale worker cannot change another worker's attempt.
- The default classifier does not persist raw exception text.
- A host classifier can produce a safe custom message.
- SQL integration tests cover new and upgraded schemas.
