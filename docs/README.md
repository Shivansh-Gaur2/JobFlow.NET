# JobFlow.NET guides

These guides explain how to use JobFlow.NET and the guarantees it makes.

- [Getting started](getting-started.md) — install the package, register SQL Server, and enqueue a job.
- [Delivery and renewable leases](delivery-and-leases.md) — how Worker A and Worker B safely share jobs.
- [Idempotent job handlers](idempotent-job-handlers.md) — how to make duplicate delivery safe.
- [Configuration](configuration.md) — lease and renewal settings.
- [Troubleshooting](troubleshooting.md) — common local-development and SQL Server problems.

JobFlow.NET is currently a preview project. Read the delivery and idempotency guides before using it for any external side effect.
