---
type: backlog
status: active
tags: [backlog]
project: spintax-dotnet
---

# TODO

## Now

- [x] First push to `github.com/investblog/spintax-dotnet` (2026-08-23, CI green on the first run).
- [x] Trusted Publishing policy on nuget.org (owner `investblog`, repo `spintax-dotnet`, workflow
      `release.yml`, environment `nuget`, packages `Spintax.*`) + the `NUGET_USER` variable;
      `v0.1.0` published 2026-08-23 (run 32600562685: "Your package was pushed", release on GitHub).
- [ ] After the first package: reserve the `Spintax.*` ID prefix on NuGet.
- [ ] Release `0.1.1` — the splice fix (#1: a `%var%` inside `{…}`/`[…]` is spliced as text before
      the split; corpus 277/277 on both hosts, 2026-09-12). A behaviour fix toward the family
      contract is a patch (RELEASING.md); the bump commit is the release decision, not part of the fix.

## Debts

- `#include` has a resolver hook (`RenderOptions.IncludeResolver`) but `ValidateOptions.KnownIncludes`
  left empty reports no `include.unknown-target` — parity with the reference, which has the same
  gap; an upstream question for `spintax-js`.
- Per-element permutation separators make `MaxLength` an upper bound (documented in `Census`).
- `Combinations` counts choice paths: `{a|a}` is 2. Documented; a distinct-text count would need
  output collisions, which the family does not define.
- **A `#def` value carrying a `|` inside a construct is still counted as one option.** `Census`
  models the splice for the text it knows — the row's values and `#set` macros — but a `#def` is
  rolled once per render and its rolled text is unknowable to a static walk, so its reference stays
  literal in the re-read body. Measured 2026-09-12 with the splice fix; the render is right, the
  count is the one that lags.
- **A variable bomb reaches `Engine.Combinations` unbounded** — `#set %a% = %b% %b%` over
  `#set %b% = %a% %a%` doubles the walk at every level and the depth-50 cap is 2^50 nodes, so the
  process dies. Measured on `7e05cff` (before the splice fix) and unchanged by it: `Render` has the
  1 MiB expansion budget, `Census` has one only for the text it expands. Give the symbolic `#set`
  walk the same allowance.
- A `%var%` in `minsize=` / `maxsize=`, and a `{?…}` branch carrying a `|` with no reference beside
  it, are not spliced: the construct is not marked. Both match `@spintax/core` 0.7.0 exactly, and
  no corpus case pins either — a family question for `spintax-js`, not a unilateral .NET change.
