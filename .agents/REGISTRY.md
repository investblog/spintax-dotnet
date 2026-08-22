# REGISTRY — adaptation log

WHY something was attached or changed. The WHAT graph lives in `map.yaml`; do not duplicate it
here.

## 2026-08-22 — bootstrap by extraction

This repository is the engine carved out of `spintax-zenno` (commit `c20722f`), where it was
built and measured as M1 of that project. Domain: **coding**; `base` on top. The chain was
**copied from `spintax-zenno`'s adapted `.agents/`, not from the library**, for one reason: the
library (`~/.agents` at `059b4c7`) has no C# rule, and `spintax-zenno` authored `quality-csharp`
and the C# section of `git-quality-gate/gate.sh` at its M1 step 0. Taking the adapted copies keeps
the two repositories' gates identical — the same five commands, the same baseline semantics.

- **MCP `spintax` — project-bound, kept.** `@spintax/mcp` is the reference engine behind a tool
  surface: the second oracle when a corpus case and this engine disagree. Rendered as
  `cmd /c npx -y @spintax/mcp` (native Windows cannot spawn bare `npx`).
- **The hook shell is Git-for-Windows bash, resolved at run time** by `.agents/hooks/bash.cmd`
  (next to `git.exe` on PATH) — a public repository cannot carry one machine's drive path, and
  bare `bash` may be the WSL launcher. `.mcp.json` stays the Windows form (`cmd /c npx`) because
  that is where this engine's first host lives; on POSIX replace it with `npx -y @spintax/mcp`.
- **Not carried over:** the `content` and `research` domains and their chains (they served the
  article and the site page, which stay in `spintax-zenno`), `docs-frontmatter` is kept because
  `project-docs` is base.
