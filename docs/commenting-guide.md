# Code commenting guide

All documentation and code comments in this repository are written in English.

## Public API documentation

Every public type, member, extension method, option, and contract must have XML
documentation. Describe what the API means to an integrator, including
ownership, trust source, idempotency, and failure behavior where relevant.

Use `<param>`, `<returns>`, `<exception>`, and `<remarks>` when they add
information an IDE user needs. Do not merely repeat the C# signature.

## Implementation comments

Comments should explain why an invariant exists: security boundaries, replay
rules, validation decisions, lock or transaction requirements, and deliberately
unsupported fallback behavior. Avoid comments that narrate syntax.

Update comments when behavior changes. A stale safety comment is worse than no
comment because it creates a false integration contract.
