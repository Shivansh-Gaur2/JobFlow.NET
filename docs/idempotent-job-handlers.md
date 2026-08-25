# Idempotent job handlers

An idempotent handler gives the same final result even if JobFlow.NET delivers the job more than once.

This matters because a worker can finish an external action and crash before it marks the job completed. After the lease expires, another worker may retry that job.

## Use a stable business key

Do not use the worker ID or the lease token as the idempotency key. Those change when a job is reclaimed.

Use a value that means the same business operation, for example:

- `payment:order-123` for charging one order;
- `welcome-email:user-456` for sending one welcome email;
- `invoice:invoice-789` for creating one invoice.

## Example: payment

Pass the order ID in the job payload. Before charging, create an operation record with a unique constraint on the order ID. Only the request that successfully creates that record is allowed to call the payment provider.

Also send the same business key to the payment provider as its idempotency key when the provider supports it.

The important idea is simple: both your database and the external provider should be able to recognise that this exact business action was already requested.

## Example: email

Store a row such as `WelcomeEmailSent(UserId)` with a unique constraint on `UserId`.

- If the insert succeeds, this handler is the first sender.
- If the insert already exists, treat the job as already handled and finish successfully.

For important emails, use an outbox-style record instead of marking it sent before the mail provider accepts it. That lets you record the request, send it, and retry safely.

## Avoid these mistakes

- Do not assume a lease means exactly-once execution.
- Do not use a random value generated during each retry as the idempotency key.
- Do not charge first and only then decide whether the order was already charged.
- Do not ignore the job cancellation token. Losing a lease means the dispatcher will request cancellation.

Idempotency belongs to the business action. JobFlow.NET protects job ownership; your handler protects the external result.
