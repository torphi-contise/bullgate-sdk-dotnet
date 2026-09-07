# Packaging and versioning

## Artifacts

The repository produces two independent NuGet packages:

- `Bullgate.Access.AspNetCore`
- `Bullgate.Billing.AspNetCore`

Each package embeds its own README and XML documentation file. Package metadata
points to the canonical Torphi Contise repository and declares Apache-2.0.

## Versioning

The current version is `0.1.0`. Until 1.0, incompatible public API changes may
occur in a minor version. Patch releases should remain backward compatible and
contain fixes or documentation corrections.

A version number in a project file does not prove that a package was published.
A release is available only after its package artifact, tag, and release notes
exist in the approved distribution channel.

## Distribution status

No public NuGet distribution channel has been approved or populated. Local
`ProjectReference` use is valid for the integration workspace but is not a
portable release strategy for isolated builds.

Selecting a registry, publishing packages, creating tags, and announcing a
release are separate publication actions. This repository may be prepared and
tested before those actions are authorized.
