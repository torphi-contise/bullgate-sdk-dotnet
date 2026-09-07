# Contributing to Bullgate SDK for .NET

Thank you for helping improve the Bullgate .NET integration packages.

## Before opening a change

- Read the required documents in [`docs/README.md`](docs/README.md).
- Search existing issues and pull requests.
- Open a design discussion before changing a public API, HTTP contract, cookie
  behavior, credential format, identity boundary, entitlement replay rule, or
  package dependency.
- Report vulnerabilities privately according to [`SECURITY.md`](SECURITY.md).

## Development setup

Install .NET SDK 10 and run:

```powershell
dotnet restore Bullgate.Sdk.DotNet.slnx
dotnet build Bullgate.Sdk.DotNet.slnx --configuration Release --no-restore
dotnet test Bullgate.Sdk.DotNet.slnx --configuration Release --no-build
dotnet pack Bullgate.Sdk.DotNet.slnx --configuration Release --no-build
```

See the [testing guide](docs/testing.md) for suite scope, focused execution,
current limitations, and expectations for new tests.

Do not commit credentials, session tokens, flow capabilities, store proofs,
personal data, generated packages, or local test results.

## Design rules

- Keep Access and Billing packages independently usable.
- Keep consumer profiles, authorization, and business persistence in the
  consumer application.
- Derive Billing customer references from trusted server sessions.
- Preserve strict response validation and explicit failure semantics.
- Require atomic, durable entitlement replay protection from consumers.
- Do not add a retry that can duplicate payment or benefit delivery.
- Do not expose server credentials or bearer material to application clients.
- Do not add product-specific types or rules to the generic SDK.

## Documentation and comments

All documentation and code comments must be in English. Public APIs require XML
documentation. Comments should explain ownership, trust, idempotency, failure,
or security behavior rather than restating syntax.

Update the root README, package README, canonical guide, and project status when
a public contract or availability claim changes.

## Pull requests

Keep each pull request focused. Describe the affected boundary, compatibility
impact, security and retry implications, validation performed, and documentation
changed. Review the final diff for unrelated files and secrets.

## Developer Certificate of Origin

Contributions use [DCO 1.1](DCO). Every contributed commit must include:

```text
Signed-off-by: Full Name <email@example.com>
```

Use `git commit --signoff` to add the trailer. It certifies provenance under
the project license and is not a copyright assignment.

Participation is governed by [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).

Maintainers preparing a package must follow the
[release process](docs/release-process.md). That document does not authorize
tag creation or registry publication.
