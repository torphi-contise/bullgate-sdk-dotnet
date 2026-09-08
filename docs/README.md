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

## Access onboarding

Start a first Access integration with
[Access getting started](access-getting-started.md), then follow the canonical
reading order above for the relevant contract.

## Contributor references

- [Changelog](../CHANGELOG.md)
- [Packaging and versioning](packaging-and-versioning.md)
- [Release process](release-process.md)
- [Testing](testing.md)
- [Code commenting guide](commenting-guide.md)
- [Open-source governance](open-source-governance.md)
- [Contributing](../CONTRIBUTING.md)
- [Security policy](../SECURITY.md)

## Access AI and retrieval references

Use these references when indexing the repository or answering questions about
`Bullgate.Access.AspNetCore`:

1. [AI grounding context](ai/context.md)
2. [Controlled glossary](ai/glossary.md)
3. [AI retrieval and answer guide](ai/retrieval-guide.md)
4. [Access decision catalog](decision-catalog.md)
5. [Baseline AI evaluation set](ai/evaluation-set.md)

The AI corpus is intentionally scoped to the Access package. Do not apply its
package-specific contracts to another package without retrieving that package's
own documentation and source.

## Trust order

When documentation and implementation disagree, use this order:

1. current source and tests for implemented behavior;
2. package-specific README files for supported integration examples;
3. this documentation set for architecture, ownership, and operational rules;
4. roadmap statements only for future intent.

Do not describe planned distribution, an unreleased package, a remote service,
or a consumer-specific integration as generally available.

## Status language

- **Implemented** — present in the current source tree.
- **Validated** — directly exercised by the stated test or environment.
- **Decided** — an accepted contract that is not fully implemented.
- **Planned** — a possible future direction, not a current capability.
- **Out of scope** — intentionally not owned by this SDK package.

Never replace these labels with vague claims such as "supported" when the
actual state matters.

## Documentation maintenance

This map is closed. Add every new canonical document here in the appropriate
section. When an answer-visible Access contract changes, update the grounding
context, glossary or decision catalog as applicable, and add or revise an AI
evaluation case.
