---
type: backlog
status: active
tags: [backlog]
project: spintax-dotnet
---

# TODO

## Now

- [x] First push to `github.com/investblog/spintax-dotnet` (2026-08-23, CI green on the first run).
- [ ] Trusted Publishing policy on nuget.org (owner `investblog`, repo `spintax-dotnet`, workflow
      `release.yml`, environment `nuget`) + the `NUGET_USER` secret; then tag `v0.1.0` (RELEASING.md).
- [ ] After the first package: reserve the `Spintax.*` ID prefix on NuGet.

## Debts

- `#include` has a resolver hook (`RenderOptions.IncludeResolver`) but `ValidateOptions.KnownIncludes`
  left empty reports no `include.unknown-target` — parity with the reference, which has the same
  gap; an upstream question for `spintax-js`.
- Per-element permutation separators make `MaxLength` an upper bound (documented in `Census`).
- `Combinations` counts choice paths: `{a|a}` is 2. Documented; a distinct-text count would need
  output collisions, which the family does not define.
