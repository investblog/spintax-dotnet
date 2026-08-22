---
type: decision
status: accepted
tags: [corpus, ci, parity]
project: spintax-dotnet
---

# 0001 — The golden corpus is read from a checkout, never vendored

**Date:** 2026-08-21 (in spintax-zenno, M1 step 0); carried over unchanged 2026-08-22.

## Context

The whole claim of this engine is parity with `@spintax/core`, `spintax/core`, `spintax-core` and
`spintax-win`. Parity rests on one artifact: the JSON fixtures in
`spintax-js/packages/conformance/fixtures/`. Every sibling settled the same question the same way —
PHP ("a copy would drift, and a drifting contract is not a contract"), Python (`SPINTAX_FIXTURES`
with a sibling-checkout fallback), Pascal (`check-corpus.sh` fails when the fixtures are absent) —
and the corpus README says every consumer reads the files from a checkout.

## Decision

Not vendored. The runner takes the path from `SPINTAX_FIXTURES`; without it, it tries the sibling
`../spintax-js/packages/conformance/fixtures`; CI checks out `investblog/spintax-js` into
`.corpus`. No corpus ⇒ exit 2 — never a green run over nothing.

The runner asserts all 258 cases: the 254 deterministic ones and the 4 `kind:rng` ones, the latter
through the engine's RNG seam with an injected strategy (within-engine reproducibility is what they
assert). `tests/known-failures.txt` is a baseline in the Pascal port's sense: a new failure is a
regression, a case that starts passing must be removed from the file in the same commit. It is
empty.
