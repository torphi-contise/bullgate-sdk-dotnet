# Access AI retrieval and answer guide

## Purpose and scope

This guide defines how a retrieval system should index and answer questions
about `Bullgate.Access.AspNetCore`. Retrieval context is not application
authorization, runtime evidence, security approval, or a replacement for human
review.

The corpus is intentionally package-specific. Do not use Access SDK material to
infer the behavior, maturity, or release state of another package.

## Corpus order

Index these sources with descending authority:

1. current Access package source under `src/Bullgate.Access.AspNetCore/`;
2. current Access package tests under `tests/Bullgate.Access.AspNetCore.Tests/`;
3. generated XML documentation and the package-specific README from the same
   build;
4. canonical documents listed in `docs/README.md`;
5. tracked examples, issues, pull requests, and historical discussions.

Keep the repository revision with every chunk. Do not combine behavior from one
revision with status, package contents, or tests from another without naming the
mismatch. A registry artifact is evidence only for its immutable package
version, not for the current checkout.

## Chunking

- Split Markdown by heading and retain the full heading path.
- Keep a table with its heading and introductory paragraph.
- Keep XML documentation with the declaration it describes.
- Keep an endpoint mapping with its request, response, cookie, authentication,
  and failure behavior.
- Keep a test name with the arrangement and assertions that establish the fact.
- Keep ownership, security, idempotency, and failure invariants together with
  their rationale.
- Prefer chunks that answer one question without hidden cross-file context.
- Do not index credentials, local secrets, generated test results, or package
  caches as documentation.

Recommended metadata:

```json
{
  "platform": "bullgate",
  "repository": "bullgate-sdk-dotnet",
  "component": "access-sdk-dotnet",
  "componentType": "server-sdk",
  "package": "Bullgate.Access.AspNetCore",
  "revision": "git commit",
  "path": "docs/access-integration.md",
  "heading": "Session behavior",
  "sourceKind": "canonical-doc",
  "authority": 4,
  "statusDate": "2026-09-07"
}
```

For source, tests, generated XML, and immutable package artifacts, use a
different `sourceKind` and the matching authority. Include a package version
only when the chunk came from that exact package artifact.

## Compatibility with a federated Bullgate corpus

A future Bullgate-wide chatbot may retrieve several repositories. Preserve the
component and repository metadata above so results do not collapse service,
SDK, consumer, and deployment facts into one namespace.

- Route an adapter or ASP.NET Core integration question to
  `component=access-sdk-dotnet`.
- Route an Access domain, persistence, provider, or hosted-runtime question to
  the matching Access service corpus.
- Treat cross-repository links as routing hints, not copied authority.
- Require revision-compatible evidence before combining service and SDK
  behavior into one answer.
- When one operation crosses components, state each owner and each independent
  failure boundary.
- Never let a newer document about one component silently override current
  executable evidence from another component.

## Retrieval strategy

1. Confirm that the question concerns `Bullgate.Access.AspNetCore`.
2. Classify it as status, setup, ownership, BFF API, session authentication,
   AccessFlow, failure, security, compatibility, troubleshooting, or release.
3. Retrieve `docs/ai/context.md`, the glossary entry for ambiguous terms, and
   the routed task-specific document.
4. Retrieve current source and tests for exact runtime claims.
5. For a route question, retrieve its complete section from
   `docs/access-http-api.md` and the corresponding endpoint mapping.
6. For a failure question, retrieve the exception classification, cookie side
   effects, and the relevant test together.
7. For a release or availability question, distinguish the public repository,
   local project version, CI artifact, registry artifact, hosted service, and
   consumer deployment.
8. If sources disagree, report the mismatch and prefer current executable
   source and tests for behavior. Do not silently rewrite historical evidence.
9. If the question is about Access service internals rather than adapter
   behavior, identify the boundary and retrieve the service repository instead
   of inventing an SDK answer.

## Answer rules

- Lead with the direct answer.
- Name the owning component when responsibility is ambiguous.
- Distinguish **Implemented**, **Validated**, **Decided**, **Planned**, and
  **Out of scope**.
- Distinguish SDK behavior from Access service behavior and consumer behavior.
- Cite repository paths and headings for non-trivial claims.
- Include the repository revision for security-, compatibility-, or
  status-sensitive answers.
- Preserve public type names, route paths, error codes, cookie names, and JSON
  fields exactly.
- Use visibly synthetic values in examples and never output bearer-shaped
  placeholders that could be mistaken for real credentials.
- Treat a test as evidence for the tested condition, not for a deployment or
  live external service.
- State uncertainty or a missing contract instead of filling it with framework
  convention.

## Cross-boundary questions

The SDK documents how the adapter transports and protects Access contracts. It
does not redefine Access domain rules. For questions about realms, provider
validation, proof authority, persistence, or service-side idempotency, retrieve
the matching Bullgate Access service documentation and revision.

The consumer owns product data, authorization, CSRF policy, deployment, and
cross-database workflows. Do not attribute those choices to the SDK merely
because the SDK exposes an extension point.

## Unsafe or unsupported requests

Refuse to reveal or reconstruct integration credentials, session tokens, reset
tokens, provider credentials, OTPs, flow capabilities, or private personal
data. A public application-client key does not grant server authorization.

Do not recommend bypassing online introspection, accepting registration-purpose
sessions as product authentication, trusting a client-supplied local subject,
returning bearer material in JSON, disabling response validation, or clearing
cookies to hide upstream availability failures.

Operations that change identity data, revoke access, delete accounts, publish a
package, or alter production configuration still require the caller's normal
authorization and approval mechanisms.

## Evaluation maintenance

Run the cases in `docs/ai/evaluation-set.md` when the corpus, chunking,
retrieval model, public contracts, failure behavior, or release status changes.
Each changed answer-visible contract must update its required facts, prohibited
claims, sources, and revision evidence.
