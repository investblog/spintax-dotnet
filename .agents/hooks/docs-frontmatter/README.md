# docs-frontmatter

A PostToolUse agent hook — **non-blocking** nudge that a doc written under a `docs/` tree
carries the `project-docs` frontmatter (`type` / `status` / `tags` / `project`). Ties to the
`project-docs` rule: the rule *asks* for metadata-as-index ("read frontmatter first, filter by
type/tags/status"); this hook *reminds* the moment a doc is saved without it. It only reports —
always exits 0, never blocks an edit (doc metadata is advisory, not a hard invariant).

**Why it exists.** Adoption was found to track doc *age*, not discipline: projects born before
the convention (booking, casino-platform, 301, …) sit at 0% frontmatter, while `task_center`
(written after) is 100% incl. ADRs. Nothing surfaced a missing header at write time, so old
bases never got retrofitted one doc at a time. This hook closes that gap going forward.

Scope: only `*.md` inside a `docs/` folder; the copied library layer (`.agents/**`) is skipped
(those carry their own `rule-format` frontmatter). Warns when the block is absent, or present but
missing a required key. **Model to copy: `task_center/docs`** (frontmatter on every doc + ADR).

## Per-agent install (bootstrap, step 4 — config assembly)
- **Claude** → merge `claude.json` into `./.claude/settings.json` (`hooks.PostToolUse`) — the
  COMMITTED file (git-pinned; `settings.local.json` is gitignored, personal `permissions` only).
- **Codex** → merge `codex.toml` into `./.codex/config.toml` (parses the `apply_patch` patch for
  changed files). Verify the `matcher` tool name for your codex version.
- **opencode** → copy `opencode.ts` to `./.opencode/plugin/docs-frontmatter.ts`.

`check.sh` is the shared checker (path + codex-patch parsing, frontmatter validation); the
per-agent files wire it in. Opt-in trace: `AGENTS_DEBUG=1`.
