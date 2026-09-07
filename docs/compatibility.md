# Compatibility

This document defines the compatibility claims that can be made from the
current repository. It distinguishes declared targets from combinations that
have actually been validated.

## Current matrix

| Dimension | Current state | Meaning |
| --- | --- | --- |
| Package versions | `0.1.0`, unreleased | The project versions exist locally; no public package compatibility commitment exists yet. |
| Target framework | `net10.0` | Consumers must target a framework compatible with .NET 10. |
| ASP.NET Core | `Microsoft.AspNetCore.App` for .NET 10 | Both packages require the ASP.NET Core shared framework. |
| Repository SDK | .NET SDK `10.0.400` | This is the pinned development and CI feature band. A later compatible feature band may be selected by the configured roll-forward policy. |
| Runtime identifier | None | The packages do not publish platform-specific runtime assets. |
| AccessFlow protocol | Version 1 | The Access adapter advertises and handles protocol version 1. |
| Package relationship | Independent | Access and Billing do not reference each other and do not require matching package versions. |
| Public distribution | None | Compatibility cannot be inferred from a local package version until that artifact is published. |

## Framework compatibility

The packages compile for `net10.0` only. .NET 8 and .NET 9 are not declared
targets and must not be described as supported merely because some source might
compile after modification.

Both packages use `FrameworkReference Include="Microsoft.AspNetCore.App"`.
Their runtime APIs come from the ASP.NET Core shared framework rather than
runtime NuGet dependencies. Test packages are development-only dependencies and
are not included in the consumer package dependency graph.

A future additional target framework is a deliberate compatibility change. It
requires its own build and test matrix, package inspection, documentation, and
changelog entry before it is called supported.

## Operating systems

The package projects contain no runtime identifier or platform support
attribute and are intended to remain operating-system neutral.

The current local verification was performed on Windows. The repository CI is
configured to build, test, and pack on Linux, but that workflow has not yet run
in the future public repository. Linux should be described as validated only
after a successful public CI run. macOS has not been validated by this
repository.

Operating-system neutrality does not guarantee that a consumer's database,
reverse proxy, secret provider, identity provider, or native store integration
supports the same platforms.

## Bullgate service compatibility

Package version and HTTP contract version are different concepts.

- The `/bullgate/access/v1` BFF prefix identifies version 1 of the
  consumer-facing HTTP surface. It does not mean package version 1.0.
- AccessFlow has its own negotiated protocol version. The current SDK constant
  is `BullgateAccessDefaults.AccessFlowProtocolVersion = 1`.
- The Access integration credential determines the remote realm and
  environment; it does not select an SDK version.
- Access and Billing package versions may advance independently because the
  packages have no code dependency on each other.

Until service releases have their own versioned compatibility guarantees, the
SDK must validate responses strictly and treat unknown or malformed successful
responses as availability failures. A deployment should validate the exact SDK
and service revisions it intends to operate together.

Billing remains in active development. Its service compatibility claims will be
curated during the dedicated Billing documentation work.

## Consumer contract compatibility

The Access package is generic over consumer-owned `TRegistration` and
`TApplication` types. Their JSON shape and persistence rules belong to the
consumer. Changing those types can break that consumer's clients without
changing the Bullgate package API.

The consumer implementation of `IBullgatePrincipalResolver` determines the
local authenticated subject and claims. The implementation of
`IBullgateAccessApplication<TRegistration,TApplication>` determines product
profile provisioning and resolution. Package compatibility does not guarantee
compatibility with a consumer's schema or authorization policy.

Cookie names and SameSite behavior are configurable. Changing them can affect
sessions independently of package binary compatibility.

## Version compatibility before 1.0

Each package follows Semantic Versioning independently:

- a patch release, such as `0.1.1`, contains backward-compatible fixes or
  package-delivered documentation corrections;
- a minor release, such as `0.2.0`, adds capability and is also the minimum
  bump for a breaking public change during the `0.x` period;
- prereleases use identifiers such as `0.2.0-alpha.1`, `0.2.0-beta.1`, and
  `0.2.0-rc.1`;
- version `1.0.0` will mark the first stable public compatibility contract.

The project should still avoid unnecessary breaking changes before 1.0. Every
breaking change requires a changelog entry and migration instructions.

Repository-only documentation may change without a package version. If a README
or XML documentation file embedded in a package is republished, it requires a
new package version because an existing registry version must never be
overwritten.

## Support policy

Before the first public package release, only the current default branch is
maintained. Local artifacts and arbitrary commits are development snapshots.

A multi-version support window and end-of-life schedule have not been decided.
They must be documented before the project claims maintenance for more than the
latest released line.

See [Packaging and versioning](packaging-and-versioning.md) for artifact rules
and the [changelog](../CHANGELOG.md) for package-specific release history.
