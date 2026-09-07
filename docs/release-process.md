# Release process

This document defines how a Bullgate .NET package moves from source to a
versioned public artifact. It is a release policy, not authorization to publish.

No package registry has been selected and no release workflow currently
publishes artifacts. The existing CI workflow only restores, builds, tests,
packs, and uploads temporary workflow artifacts.

## Release units

The repository contains two independent release units:

| Package | Project | Tag format |
| --- | --- | --- |
| `Bullgate.Access.AspNetCore` | `src/Bullgate.Access.AspNetCore/Bullgate.Access.AspNetCore.csproj` | `access-vX.Y.Z` |
| `Bullgate.Billing.AspNetCore` | `src/Bullgate.Billing.AspNetCore/Bullgate.Billing.AspNetCore.csproj` | `billing-vX.Y.Z` |

Releasing one package does not require changing or publishing the other. A tag
identifies exactly one package release even when both project files happen to
contain the same version.

Examples:

```text
access-v0.1.0
access-v0.2.0-beta.1
billing-v0.1.0
```

Use only a valid Semantic Versioning value after the package prefix.

## Current release status

Both project files currently declare `0.1.0`, but both changelog entries are
marked **Unreleased**. There are no repository tags or public package releases.

The Access package has an English package README, generated XML documentation,
a documented HTTP surface, compatibility policy, troubleshooting guide, and
passing automated tests.

Billing remains in active development. Its release contents must not be frozen
or described as ready until its dedicated documentation and validation work is
complete.

## Choose the next version

Select the version for the package being released:

- patch, such as `0.1.1`, for backward-compatible fixes or package-delivered
  documentation corrections;
- minor, such as `0.2.0`, for new capability or a breaking change during the
  `0.x` period;
- prerelease, such as `0.2.0-beta.1`, when consumers must validate the
  artifact before a final release;
- `1.0.0` only after the stable public compatibility boundary is approved.

Repository-only documentation does not require a package version. Documentation
embedded in the package, including its README and XML file, becomes part of the
artifact and requires a new version when republished.

## Prepare a release change

The release change for one package must:

1. update that package's `Version` value;
2. move its changelog entry from **Unreleased** to the same version and add the
   release date;
3. describe every integrator-visible addition, change, deprecation, removal,
   fix, and security correction;
4. include migration instructions for every breaking change;
5. update package README, XML comments, compatibility claims, and technical
   documentation when affected;
6. leave the other package version unchanged unless it is independently being
   released;
7. contain no generated package, test result, credential, local configuration,
   or unrelated change.

The pull request description must identify the intended package and version.
The final source commit must be present on the protected public `main` branch
before a release tag is created.

## Required validation

Run validation from the exact release candidate commit:

```powershell
dotnet restore Bullgate.Sdk.DotNet.slnx
dotnet build Bullgate.Sdk.DotNet.slnx --configuration Release --no-restore --no-incremental
dotnet test Bullgate.Sdk.DotNet.slnx --configuration Release --no-build
```

Then pack only the intended release unit.

For Access:

```powershell
dotnet pack src/Bullgate.Access.AspNetCore/Bullgate.Access.AspNetCore.csproj --configuration Release --no-build --output artifacts/packages
```

For Billing:

```powershell
dotnet pack src/Bullgate.Billing.AspNetCore/Bullgate.Billing.AspNetCore.csproj --configuration Release --no-build --output artifacts/packages
```

Before publication, verify:

- the package filename, ID, and version agree;
- the NuGet manifest declares Apache-2.0 and the canonical repository;
- the repository commit recorded in the manifest is the release candidate;
- the package contains only its README, assembly, XML documentation, NuGet
  metadata, and deliberately approved future assets;
- the package contains no source secrets, test artifacts, or unrelated package;
- the package installs, restores, and builds in a temporary clean consumer
  project using the local artifact;
- public API compatibility matches the selected version bump;
- CI passes on the protected public commit.

A checksum should be recorded for the exact artifact selected for publication.
Do not compare a locally rebuilt package with a registry artifact and assume
identity unless deterministic inputs, toolchain, commit, and package contents
were controlled.

## Tag the approved commit

Create a package-specific annotated tag only after all release checks and
approvals succeed:

```text
access-v0.1.0
billing-v0.1.0
```

The version in the tag, project file, package manifest, changelog heading, and
release title must match exactly. Never move or reuse a published tag.

The intended GitHub release titles are:

```text
Bullgate.Access.AspNetCore 0.1.0
Bullgate.Billing.AspNetCore 0.1.0
```

A prerelease tag must create a prerelease, not a stable release.

## Build from the tag

The future release workflow should check out the immutable tag and rebuild the
package instead of publishing a developer workstation artifact. It must:

1. use the pinned .NET SDK;
2. restore from the committed configuration;
3. build with warnings as errors;
4. run the complete test suite;
5. pack only the package identified by the tag prefix;
6. inspect package identity and version against the tag;
7. retain the package and checksum as release evidence;
8. require explicit approval before publishing to the registry.

A tag whose prefix does not map to exactly one known project must fail without
publishing.

## Registry publication

Registry selection is still pending. The repository must not contain a working
publish command, registry credential, or release secret until the destination
and access model are approved.

Before enabling publication, decide and document:

- the public registry and package namespace ownership;
- whether GitHub Releases also retain package artifacts;
- trusted publishing or another short-lived authentication mechanism;
- approval environment and authorized maintainers;
- package signing policy;
- symbol package policy;
- provenance, checksum, and retention requirements;
- recovery procedure for a compromised publishing identity.

Prefer a release environment with minimum permissions and human approval.
Ordinary pull-request CI must remain read-only and must never receive a package
publishing credential.

## Publish the release record

After the registry accepts the exact version:

1. create the GitHub release from the package-specific tag;
2. use the matching changelog section as release notes;
3. link the registry artifact and relevant migration guide;
4. record the package checksum and CI run;
5. verify that a clean consumer can restore the package by ID and exact version;
6. update project status only after the public artifact is observable.

Do not announce a release merely because CI created a temporary package
artifact.

## Failure and correction

Package versions and published tags are immutable. Never overwrite a published
version or move its tag.

If a release is defective:

- stop promotion and announcement;
- document the impact;
- deprecate or unlist the version only when the selected registry policy and
  severity justify it;
- preserve the release record and explanation;
- publish the correction under a new version;
- rotate publishing credentials if compromise is suspected.

A failed publication attempt does not become a release. Keep the changelog entry
unreleased unless the artifact became publicly retrievable.

## First-release gates

Before the first public package release, the project still needs:

- an approved package registry and verified namespace ownership;
- a release workflow separated from read-only pull-request CI;
- a clean consumer installation smoke test;
- an approved public API compatibility baseline;
- successful CI on the canonical public repository;
- explicit authorization for the exact tag and package publication.

Repository publication and package publication are separate actions. Making the
source public does not authorize creating a package release.

See [Packaging and versioning](packaging-and-versioning.md),
[Compatibility](compatibility.md), the [changelog](../CHANGELOG.md), and the
[security policy](../SECURITY.md).
