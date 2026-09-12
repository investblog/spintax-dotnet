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
- [x] Release `0.1.1` — the splice fix (#1: a `%var%` inside `{…}`/`[…]` is spliced as text before
      the split; corpus 277/277 on both hosts, 2026-09-12). A behaviour fix toward the family
      contract is a patch (RELEASING.md).

## Debts

- `#include` has a resolver hook (`RenderOptions.IncludeResolver`) but `ValidateOptions.KnownIncludes`
  left empty reports no `include.unknown-target` — parity with the reference, which has the same
  gap; an upstream question for `spintax-js`.
- Per-element permutation separators make `MaxLength` an upper bound (documented in `Census`).
- `Combinations` counts choice paths: `{a|a}` is 2. Documented; a distinct-text count would need
  output collisions, which the family does not define.

### Census: the gaps that remain between a static count and the engine

Five rounds of the Codex gate (2026-09-12) established that "the count never understates" is not a
property this walk can hold by patching — it is a static tree walk describing a dynamic engine, and
they part company wherever the render stops expanding AND at several points inside the allowance.
The walk now SATURATES where it cannot bound an answer, and the contract is documented as a count,
not a proof. These are the known gaps, each measured; closing any of them is a design change, not a
fix. The per-element separator approximation above is a sixth, older one.

- **A `#def` whose value carries a construct, spliced into a construct.** A construct-free
  definition splices correctly — rolled once and held, so its text is known statically — but one
  holding `{a|bb}|c` is rolled per render and the splice follows the roll. `Combinations` says 2
  where the render draws 4 paths over 3 distinct texts. The `Poly` machinery models "rolled once,
  multiplied once"; splicing PER roll is a different shape.
- **A `#def` cycle's length.** `DefLength` memoises 0 to break the cycle, so `#def %a% = %a%` is
  charged nothing and measures nothing: `MaxLength("#def %a% = %a%\n%a%")` is 1 where the render
  emits `\n%a%`, 4. An accumulating cycle (`#def %a% = x%a%y`) understates further — the render
  runs to the depth cap. A cycle needs either the renderer's own unrolling or saturation, and
  saturation would cost `Combinations` an answer that is currently correct (one path).
- **`#include`d children are not counted.** The renderer charges one allowance across a document
  and its includes; `Engine.Combinations` / `MaxLength` take no resolver and never see a child. The
  count is of the template alone — now documented as such rather than implied.
- **Directive-backed variables in a plural slot or a conditional test.** `ExpandRuntimeVars` and
  `TakesThen` read the row only, while the renderer tests against the merged map — runtime, `#set`
  and rolled `#def`. A `#set` that decides a branch is therefore invisible here, so the walk can
  take a different branch from the render and skip substitutions the render charges.
- A `%var%` in `minsize=` / `maxsize=`, and a `{?…}` branch carrying a `|` with no reference beside
  it, are not spliced: the construct is not marked. Both match `@spintax/core` 0.7.0 exactly, and
  no corpus case pins either — a family question for `spintax-js`, not a unilateral .NET change.
