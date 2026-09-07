# Testing

This guide describes the repository's current automated-test scope and the
minimum validation expected from contributors. It does not claim end-to-end
validation against deployed Bullgate services or native stores.

## Test stack

The test projects use xUnit, Coverlet's data collector, and ASP.NET Core
`TestServer` on .NET 10. Outbound Access and Billing calls are intercepted by
test `HttpMessageHandler` implementations. The tests therefore exercise the SDK
adapters without sending requests to a real Bullgate environment.

Dependency restore may contact configured NuGet sources. After dependencies are
available, the test cases themselves require no Bullgate credentials, customer
data, store accounts, or deployed Bullgate services.

Values such as `bgbc_test`, `password-123`, and `*.example.test` are inert test
fixtures. Never replace them with real secrets or personal data.

## Test projects

### Bullgate Access

`tests/Bullgate.Access.AspNetCore.Tests` covers:

- exact server-to-server request construction and strict response parsing;
- domain rejection, malformed response, network failure, timeout, and caller
  cancellation behavior;
- AccessFlow capabilities, revisions, terminal sessions, and secure cookie
  handling;
- registration, login, logout, recovery, social-login management, and current
  identity operations;
- consumer-owned profile provisioning and principal resolution boundaries;
- account-deletion ownership at the consumer boundary;
- BFF input validation and prevention of token disclosure.

### Bullgate Billing

`tests/Bullgate.Billing.AspNetCore.Tests` covers the currently implemented
Billing surface, including:

- eligibility, preparation, completion, and finalization-failure contracts;
- exact store and customer bindings without accepting mobile overrides;
- entitlement delivery and reversal authentication and separation;
- one-time catalog and customer BFF behavior;
- offer preparation, administration, audit, and failure contracts;
- purchase-history validation and pagination;
- strict malformed-response handling and explicit retry semantics.

Billing remains under active development. Passing tests describe the current
implementation; they do not declare its public API or release contents frozen.

## Run the complete validation

From the repository root:

```powershell
dotnet restore Bullgate.Sdk.DotNet.slnx
dotnet build Bullgate.Sdk.DotNet.slnx --configuration Release --no-restore
dotnet test Bullgate.Sdk.DotNet.slnx --configuration Release --no-build
```

Use the pinned SDK from `global.json`. Release builds treat warnings as errors.
Running the build before `dotnet test --no-build` also ensures that generated
XML documentation and all public-API warning rules are evaluated.

## Run one package suite

Access only:

```powershell
dotnet test tests/Bullgate.Access.AspNetCore.Tests/Bullgate.Access.AspNetCore.Tests.csproj --configuration Release
```

Billing only:

```powershell
dotnet test tests/Bullgate.Billing.AspNetCore.Tests/Bullgate.Billing.AspNetCore.Tests.csproj --configuration Release
```

To focus on one test class, use a fully qualified name filter. For example:

```powershell
dotnet test tests/Bullgate.Access.AspNetCore.Tests/Bullgate.Access.AspNetCore.Tests.csproj --configuration Release --filter "FullyQualifiedName~BullgateAccessClientTests"
```

Focused execution is useful during development, but the complete solution must
pass before a change is submitted.

## Measure local coverage

Build the solution first, then collect Cobertura reports:

```powershell
dotnet build Bullgate.Sdk.DotNet.slnx --configuration Release --no-restore
dotnet test Bullgate.Sdk.DotNet.slnx --configuration Release --no-build --collect:"XPlat Code Coverage" --results-directory TestResults/coverage
```

Each test project writes a `coverage.cobertura.xml` file below the selected
results directory. `TestResults/` is intentionally ignored by Git because
coverage output is generated evidence, not source.

Review line and branch coverage together. A higher percentage is not sufficient
evidence by itself: tests should close meaningful contract, security, failure,
and state-transition gaps. Do not add assertions that merely execute code
without proving behavior.

## What these tests do not prove

The repository currently has no automated test that proves:

- connectivity to a deployed Access or Billing service;
- behavior with production or sandbox Google, Apple, or payment-store accounts;
- native mobile checkout, acknowledgement, consumption, or transaction
  recovery;
- compatibility with a particular consumer application's persistence,
  authorization, or deployment environment;
- installation from a public package registry;
- availability of any Bullgate service or package release.

Those validations require separate environments and explicit credentials. They
must not be added to ordinary pull-request tests or performed using contributor
secrets.

## Expectations for new tests

A behavior change should add or update tests at the narrowest authoritative
boundary. Tests should:

- assert the exact outbound method, path, authentication header, and body when
  the wire contract matters;
- cover successful responses, known domain rejections, malformed success and
  error responses, and transport failure where applicable;
- prove that caller-controlled input cannot override trusted server identity,
  credentials, store binding, or entitlement data;
- keep idempotency, replay, retryability, and resolution requirements explicit;
- verify that bearer material and integration credentials are not returned to
  browser or mobile callers;
- use deterministic local fixtures and avoid timing or network assumptions;
- update public documentation and the changelog when the tested contract
  changes.

Do not weaken an assertion merely to accommodate a changed implementation.
First determine whether the implementation or the documented contract is
authoritative for the intended change.

## Continuous integration

The repository CI restores, builds, tests, and packs on `ubuntu-latest`. Test
results are uploaded as build artifacts even when the test step fails. Package
artifacts are produced for inspection only; the workflow does not publish them.

The current CI workflow does not run live-service tests, publish code coverage,
or perform external static analysis. Those capabilities require separate,
explicitly approved configuration.

See [Contributing](../CONTRIBUTING.md) for the complete contribution checklist
and [Release process](release-process.md) for release-specific validation.
