# Delivery and renewable leases

JobFlow.NET uses a renewable lease to decide which worker currently owns a job.

## The basic story

Imagine two application instances: Worker A and Worker B.

1. A pending job is ready in SQL Server.
2. Worker A claims it in one SQL operation.
3. SQL Server stores a unique lease token and an expiry time with that job.
4. While A is working, it renews the lease regularly.
5. A can complete or fail the job only while its token still matches and the lease has not expired.

Worker B cannot claim that same job while A's lease is valid.

## What happens if Worker A crashes?

If A stops renewing, its lease expires. Worker B can then claim the job with a **new** token.

If A wakes up late and tries to mark the job completed, SQL Server rejects that update because A has the old token. This stops an old worker from overwriting the newer worker's state.

## Why delivery is at least once

There is still one unavoidable case:

1. Worker A sends an email or charges a card.
2. A crashes before it records job completion.
3. The lease expires.
4. Worker B runs the job again.

SQL Server cannot know whether the external action already happened. Therefore JobFlow.NET provides **at-least-once delivery**, not exactly-once delivery.

This is normal for durable background systems. The job handler must make its own side effects safe to repeat. See [idempotent job handlers](idempotent-job-handlers.md).

## What the lease does guarantee

For one stored job row, the lease token prevents:

- two valid workers from completing the job at the same time;
- an expired worker from marking a newly reclaimed job complete or failed;
- an expired worker from renewing a job after another worker owns it.

It does **not** guarantee that a side effect outside SQL Server happened only once.

## Choosing a lease duration

The default lease is five minutes and renews every minute. These are starting values, not universal truths.

- The lease duration should be longer than a normal renewal delay and short enough that crashed jobs are recovered promptly.
- The renewal interval should be comfortably shorter than the lease duration.
- A job that can run for hours should be designed to checkpoint its own progress and honour cancellation.

See [configuration](configuration.md) for how to change the values.
