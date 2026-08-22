# Contributing to JobFlow.NET

Thanks for considering a contribution. JobFlow.NET is early-stage software, so correctness and clear evidence matter more than feature count.

## Before you start

- Search existing issues before opening a new one.
- Open an issue or discussion before beginning a substantial feature or behavioural change.
- Keep each pull request focused on one concern.
- Do not include secrets, connection strings, or production data.

## Development workflow

1. Create a branch from `main`.
2. Make the smallest change that solves the agreed problem.
3. Add or update tests when the repository has a suitable test project.
4. Run the verification commands below.
5. Open a pull request using the provided template and explain the evidence for the change.

```powershell
dotnet restore JobFlow.sln
dotnet build JobFlow.sln --configuration Release --no-restore
dotnet test JobFlow.sln --configuration Release --no-build
```

## Code and design expectations

- Follow `.editorconfig` and existing project conventions.
- Preserve cancellation-token propagation and asynchronous I/O.
- Keep SQL parameterized and make concurrency assumptions explicit in code, tests, or documentation.
- Prefer a small, stable module interface that hides implementation details from callers.
- Avoid unrelated formatting or refactoring in the same pull request.

## Pull requests

Pull requests should describe the problem, the approach, how the change was verified, and any known limitations. A change that affects job execution, retry behaviour, or database claiming must include evidence that covers the relevant behaviour.

By contributing, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
