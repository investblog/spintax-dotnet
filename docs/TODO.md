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
      alone — wait for the answer and the fixtures. **Answered:** both were decided in `#80` (a
      reference or a whole `{?…}` anywhere in the header or a separator marks the construct), the
      fixtures shipped with `@spintax/core` 0.8.0, and the mirror landed here 2026-09-17.
- [x] Reported the .NET mirror on the tracking issue
      [spintax-js#78](https://github.com/investblog/spintax-js/issues/78#issuecomment-5647610154),
      including the warning `py` and `win` need: the static analysis has to make the same change as
      the renderer, and the corpus cannot see it.
- [ ] **Check `spintax-zenno` against the splice.** Its `Lint` adds each raw variable VALUE to the
      ignore list so data is not flagged as writing; a spliced list no longer appears verbatim in
      the render, so brand names from data may now be reported as findings. Raised by review here,
      never verified there — it is a sibling repo consuming this engine from a checkout.
- [x] **Mirror `@spintax/core` 0.9.0** ([#2](https://github.com/investblog/spintax-dotnet/issues/2),
      2026-09-17). The corpus on `spintax-js@main` (987b080) is 333 cases and **all 333 pass, on
      net8.0 and net472**, from PASS=297 FAIL=36. Five steps, each measured before the next:
      UCP character classes (#81, 19 cases) → the one-case TLD and the titlecase letter after a
      block tag (#79, 5) → the wider re-read key (#80, 8) → an element that renders empty is
      dropped (#80, 5, with the `Census` mirror) → the runner reads `diagnosticCount` (#74).
      The local gate and CI disagreed because they read two different corpus checkouts, not
      because the engines differed — settled by running `spintax-py` against both refs.
      Two cost fixes came with it, both measured: the shields are scanners (a 2000-label dotted
      chain 152 → 0.4 ms, Cyrillic 269 → 1.1) and the capitalizers walk the lead once (20 000
      `\n`+space 11 141 → 6.8 ms, 20 000 `<p>` 7381 → 11.9). Ordinary text is unchanged: prose
      26 ms per 1000 renders, HTML blocks 114.
- [x] **Release `0.1.3`** — the 0.9.0 catch-up (2026-09-17, tag `v0.1.3`, run 35211869880: "Your
      package was pushed" for the package and the symbols, `api.nuget.org` lists it). A patch by
      this repo's rule (`RELEASING.md`): a behaviour fix toward the family contract while 0.x, and
      no public API moved — even though render output changes for every template of the #79/#80/#81
      shapes and `Combinations` returns different numbers for a droppable element, which is more
      than the previous two patches carried. `@spintax/core` called the same work a minor (0.8.0,
      0.9.0) under a different policy; the owner chose the repo's rule. **Not verified by
      consuming the published package** — 0.1.1 was, this one only by the index listing it.

## Debts

- `#include` has a resolver hook (`RenderOptions.IncludeResolver`) but `ValidateOptions.KnownIncludes`
  left empty reports no `include.unknown-target` — parity with the reference, which has the same
  gap; an upstream question for `spintax-js`.
- Per-element permutation separators make `MaxLength` an upper bound (documented in `Census`), and
  so does an element that can render blank: the longest render drops nothing, so the full element
  list is measured. `[a|{b|}|c]` is 5 (`a b c`), which no render exceeds.
- **`RegexOptions.IgnoreCase` is not PCRE2's caseless matching, measured on both hosts 2026-09-17.**
  `\p{Ll}` under it matches `A` (JavaScript's reading, not PCRE2's); a caseless `[a-z]` takes
  U+212A on net8 and not on net472, and U+017F on neither, where PCRE2's takes both. The
  post-process domain and email shields therefore carry no `i` and spell those letters out, and
  the config patterns are correct as they are (PHP writes them without `/u`, so byte-mode caseless
  is ASCII). What is left unfixed: three patterns that still use the flag where the reference's
  carries `i` over ASCII letters — the URI scheme, the single-token abbreviations and the HTML
  closing-tag scan. Measured against `@spintax/core` 0.9.0 (the PHP engines were NOT run):

  | input | `@spintax/core` 0.9.0 | here, 0.1.3 |
  |---|---|---|
  | `visit httpſ://x.io/a,b now` | `Visit httpſ://x.io/a,b now` | `Visit httpſ: //x.io/a, b now` |
  | `see vſ. next` | `See vſ. next` | `See vſ. Next` |

  So it is not only "unshielded": the cosmetic pass then walks into the URI and spaces it. No
  corpus case pins either, and the family reads `ſ` as `s` only because JS and PCRE2 fold it under
  `i`; fixing it here means spelling `[sSſ]` out in those three patterns. No pattern that still
  carries `i` contains a `k`, so the net8/net472 difference on U+212A has no consumer today.
- **A chain of marked constructs costs more than linear, measured 2026-09-17.** A construct the
  parser marks keeps its body, and a body holds every body below it: `{%v%{%v%{…}}}` nested 500 /
  1000 / 2000 / 4000 deep (2.5–20 KB of template) renders in 22 / 29 / 95 / 273 ms and allocates
  9 / 14 / 22 / 170 MB. JavaScript pays nothing for this — V8's `slice` is a view — and .NET's
  `Substring` copies. `spintax-win` shipped a retention cap for the same shape and replaced it the
  same day with a structural rule: **a descendant of a retained construct never takes a body of its
  own**, which holds because a parent whose body did not change has nothing inside it that could
  have, and a parent that re-reads re-parses its descendants anyway. Not ported here: it needs the
  parser to clear what it has already built, and no corpus case pins it. The alternative is what
  the reference did — keep offsets into the source instead of a substring.
- Two costs left where the reference removed them, neither corpus-pinned: `Parser.LooksLikeHtmlStartTag`
  compiles a regex from the tag name on every call (the reference scans instead — it had to, V8
  refuses to compile a pattern past ~7.8 KB), and `PostProcessor.Restore` runs the per-key loop
  for `\0`-carrying input, which is O(text × placeholders) where the reference computes the same
  answer in one pass. Neither has been measured here.
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
- **A conditional inside a construct, counted across all data.** The renderer resolves `{?…}` before
  it splits, so the branch decides how many elements there are; without a row this walk cannot take
  a branch and counts the parsed tree. `[{?f?a|b|x}|c]` counts 4, where the render draws 6 paths
  when `f` is blank (the branch `b|x` splits into two elements) and 2 when it is set — measured
  2026-09-17. For a row it is exact. This is the same family as "directive-backed variables in a
  conditional test" above, with structure now at stake, not only length.
- **Which ways render blank is decided structurally.** The count of a dropped element is exact for
  literals, enumerations, conditionals (for a row), nested permutations and a construct whose
  re-read body this walk has (`[a|{%v%|}|c]` with `v = x` counts 8, the number of draws). It
  answers "not blank" where it cannot prove blank: a variable, a definition reference, a plural,
  and a marked construct whose re-read changed nothing. Each of those can only make the count
  higher than the render's.
- The two gaps that were open here at 0.1.2 — a `%var%` in `minsize=` / `maxsize=`, and a `{?…}`
  branch carrying a `|` with no reference beside it — are **closed**: `spintax-js#80` decided the
  family reads both as text before the split, and the mirror landed 2026-09-17 with the corpus
  cases that pin it.
