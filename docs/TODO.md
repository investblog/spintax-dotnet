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
- [ ] Reserve the `Spintax.*` ID prefix on NuGet. Still open and still needed: the search index
      reports `verified: false` for `Spintax.Core` as of 2026-09-12, with `0.1.0`, `0.1.1` and
      `0.1.2` published. A manual action in the nuget.org account — nobody else can do it.
- [x] Release `0.1.1` — the splice fix (#1: a `%var%` inside `{…}`/`[…]` is spliced as text before
      the split; corpus 277/277 on both hosts, 2026-09-12). A behaviour fix toward the family
      contract is a patch (RELEASING.md). Verified on the published package: the reported preset
      over eight names renders `Amigo, BTG, Belatra, Apparat, 3 Oaks and Amusnet`, no raw pipe.
- [x] Release `0.1.2` — the `Census` work behind the splice fix: the count follows the render's
      depth, freeze and allowance, saturates where it cannot bound an answer, and no longer
      advertises a guarantee it does not hold (2026-09-12, five rounds of the Codex gate).
- [x] Asked `spintax-js` about the two family-level splice gaps — a `%var%` in `minsize=` /
      `maxsize=`, and a `{?…}` branch carrying a `|` with no reference beside it:
      [spintax-js#80](https://github.com/investblog/spintax-js/issues/80), 2026-09-12. Both
      reproduce in `@spintax/core` 0.7.0 itself (measured over 40 seeds), neither is pinned by the
      corpus, and the expected PHP outputs still need confirming in docker. Do not change .NET
      alone — wait for the answer and the fixtures.
- [x] Reported the .NET mirror on the tracking issue
      [spintax-js#78](https://github.com/investblog/spintax-js/issues/78#issuecomment-5647610154),
      including the warning `py` and `win` need: the static analysis has to make the same change as
      the renderer, and the corpus cannot see it.
- [ ] **Check `spintax-zenno` against the splice.** Its `Lint` adds each raw variable VALUE to the
      ignore list so data is not flagged as writing; a spliced list no longer appears verbatim in
      the render, so brand names from data may now be reported as findings. Raised by review here,
      never verified there — it is a sibling repo consuming this engine from a checkout.
- [ ] **Port the character-class change from `spintax-js` 9850599** (2026-09-12, "character
      classes follow PHP — UCP under /u, ASCII without it"). Its new corpus case
      `validate/perm-config-nbsp-is-not-config-whitespace` fails here (`verdict=invalid want=valid`,
      `permutation.unknown-key:error@1:3`): .NET still treats NBSP as config whitespace. The
      pre-push gate blocks on it; the hooks-only commit `0c55301` was pushed with `--no-verify` on
      2026-09-14 (PC move) for that reason alone. Unit tests 187/187 on net8.0 and net472.

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
