# Bullgate SDK for .NET documentation

This directory is the canonical documentation set for this repository. Source
code and tests are authoritative for exact runtime behavior.

## Required reading

1. [Project status](project-status.md)
2. [Architecture](architecture.md)
3. [Access integration](access-integration.md)
4. [Access BFF HTTP API](access-http-api.md)
5. [Billing integration](billing-integration.md)
6. [Security model](security-model.md)
7. [Failure model](failure-model.md)
8. [Compatibility](compatibility.md)
9. [Access troubleshooting](access-troubleshooting.md)

## Contributor references

- [Changelog](../CHANGELOG.md)
- [Packaging and versioning](packaging-and-versioning.md)
- [Release process](release-process.md)
- [Testing](testing.md)
- [Code commenting guide](commenting-guide.md)
- [Open-source governance](open-source-governance.md)
- [Contributing](../CONTRIBUTING.md)
- [Security policy](../SECURITY.md)

## Trust order

When documentation and implementation disagree, use this order:

1. current source and tests for implemented behavior;
2. package-specific README files for supported integration examples;
3. this documentation set for architecture, ownership, and operational rules;
4. roadmap statements only for future intent.

Do not describe planned distribution, an unreleased package, a remote service,
or a consumer-specific integration as generally available.
