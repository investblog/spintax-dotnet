---
name: project-docs
description: Project docs, plans, decisions, knowledge — must-have, kept current, ALL in the project (never home). Apply when maintaining docs, plans, decisions, or context.
---

# project-docs

Documentation is **must-have**, not optional; kept current alongside the code. Everything is
**Markdown (`.md`) by default** — greppable, diffable, plain-text source.

[CRITICAL] Plans, docs, decisions, and intermediate work-artifacts live **ONLY in the project** —
never `~/.claude/`, `~/.codex/`, `~/.config/opencode/`, or any home/global agent folder (agent machine
state — doesn't travel with the repo, pollutes other projects). Scratch/temp → the session scratchpad
or a gitignored project dir, never home.

## Folders by audience
The dividing axis: **what we AUTHOR** (plans / decisions / docs / content — our folders) vs **what we
CONSUME** (`context/` — external-origin inputs / raw source, not authored by us). Borderline items go
by origin+role: as-received → `context/`; our derived artifact → the authored folder. **Link, don't copy.**

- `docs/` — **dev/agent source of truth** we author: architecture, contracts, flows, `docs/decisions/` (ADR). Code follows docs.
- `./.agents/plans/{active,done}` — **agent + dev plans** (+ cross-session memory in `REGISTRY.md`); each
  active plan opens with a **handoff** note (done / what did NOT work / the single next step).
- `./.agents/runbooks/` — **standing operational procedures** the agent runs verbatim (deploy, restore,
  secret-rotation); project facts, not user docs and not ephemeral plans. See `.agents/runbooks/README.md`.
- **`context/`** — **inputs we CONSUME**: briefs, requirements, references, research sources, stakeholder
  material. Read-for-context, not authored here. (A raw brief → `context/`; the plan we derive → `plans/`;
  a meeting note → `context/`, its extracted decision → `docs/decisions/`.) Heavy media → `context/attachments/` (gitignored).
- **`content/`** — **deliverables for NON-dev audiences** (business / client / marketing / published). See `content-vault`.
- **user-docs** — README + a `docs/`|`wiki/`|`guide/` folder for **product users** (see `user-docs`).
- backlog index (`ROADMAP.md` / `docs/TODO.md`) — the single list of open work → links to `plans/active/`.

## Working memory — plans & decisions (triggers, not taste)
- **Plan — REQUIRED** when the task is multi-step, touches more than one file/zone, or changes structure:
  write `./.agents/plans/active/<slug>.md` **before** editing, show it, then work **against it** (tick
  steps as you go). On completion `git mv` it to `plans/done/` — its own commit.
- **The backlog index is NOT the plan.** `docs/TODO.md` / `ROADMAP.md` holds **one line per open item**,
  linking to `plans/active/<slug>.md`. Decomposing and tracking a task *inside TODO* instead of in a plan
  loses the plan, the handoff, and the `done/` archive. **Index in TODO, work in the plan.**
- **Decision (ADR) — REQUIRED** whenever a choice is architectural, hard to reverse, or rejects a real
  alternative (stack / storage / protocol / boundary / naming that others must follow). Write it to
  `docs/decisions/`: context, decision, alternatives, dead-ends, consequences. If you catch yourself
  explaining "why X and not Y" in chat, that is an ADR — not a chat message.
- **Handoff**: every `plans/active/` file opens with *done / what did NOT work / the single next step* —
  this is the cross-session memory. `REGISTRY.md` records WHY the **environment** changed (tools, rules,
  attachments) — not task progress.
- **The project's memory is COMMITTED.** `./.agents/` — rules/skills/agents/**plans**/lock — and
  `docs/decisions/` travel with the repo (that is the point of copy-by-value, and the reason home folders
  are forbidden: they don't travel). Mind the leading dot: **`./.agents/`**, never `agents/`. Only
  regenerable caches and heavy/secret material stay out (`.codegraph/`, `.docindex/`,
  `context/attachments/`, secrets, session scratch).

## Native-memory is a SIGNPOST, not a store
An agent's built-in memory (Claude Code's `~/.claude/projects/<slug>/memory/` + `MEMORY.md`) loads every
session — which is exactly why agents keep dropping project knowledge there. But it is a **home folder:
not committed, does not travel with the repo, lost on a machine/clone change** (real incident: a
project's whole knowledge sat there and was gone off-machine). So it holds NO project knowledge —
instead, turn its `MEMORY.md` into a **redirect signpost**, the one legitimate write to `~/.claude`:
empty of content, it maps where each kind of knowledge actually lives and forbids recording there.
```
# Memory index — empty by design. Project knowledge lives IN THE PROJECT, never ~/.claude:
#   behavioral rules / lessons → AGENTS.md ("Behavioral rules")   decisions → docs/decisions/
#   plans → .agents/plans/{active,done}   infra/operational → docs/   environment WHY → .agents/REGISTRY.md
# Do NOT record project memories in ~/.claude for this project.
```
Loaded every session, the signpost redirects the next agent at the point of temptation. **Bootstrap
seeds it** (canon step 4). User-level, cross-project preferences may still live in native-memory — the
ban is on PROJECT knowledge, not on the feature.

## Confidential input — the exception, not the default
`context/` is committed (it is the project's input, and it must travel). **Confidentiality is a
property of an individual input, not of the folder** — a client's org chart or an unredacted export,
not the downloaded spec next to it. Do not gitignore `context/` wholesale because one file is sensitive.

When an input IS confidential, it is a secret file in every practical sense — same discipline:
- the **raw** form stays local (gitignored by an explicit, separately-commented path — never folded
  into the same ignore block as the project's memory);
- the form the project actually needs is **derived and sanitized**, and by that act it stops being an
  input: it moves to the authored side (a seed, a fixture, a doc) and is committed there. This is the
  same move as a template beside a secret file — key names travel, values do not.

Never let one ignore block cover both a confidential input and the project's memory (`plans/`,
`REGISTRY.md`, `docs/decisions/`). They look alike ("local"), but memory MUST travel — a real
incident lost a project's REGISTRY on a machine change exactly this way.

## Metadata, links, retrieval
- **Frontmatter** on docs/notes, so the agent filters by metadata without reading the full text:
  `type:` · `status:` (active|in_progress|done|archived) · `tags: [...]` · `project:`. Type vocabulary:
  `reference` (architecture / infra / source-of-truth) · `spec` (specification / ТЗ) · `runbook`
  (step-by-step ops procedure) · `guide` (role how-to) · `decision` (ADR / manifest) · `research` ·
  `note` · `plan`.
  A `docs-frontmatter` PostToolUse hook *reminds* when a `docs/*.md` is saved without it (a nudge, not a
  gate). Model to copy: `task_center/docs` (frontmatter on every doc + ADR). **Filenames — kebab-case.**
- **Links — standard Markdown relative links** `[text](path.md)` (portable, render on GitHub, exact path).
  NOT `[[wiki-links]]` (Obsidian-only — plain text on GitHub) unless the project actually runs Obsidian.
- **Diagrams — inline as code, not linked files (default).** Embed the diagram *source* as a fenced
  ` ```mermaid ` block in the doc — single source of truth, diffable, GitHub renders it natively. NOT
  `![](diagram.svg)` links to external image files (orphan assets, drift). Render to an image only at
  **export** time (PDF/docx — see `md2pdf`); externalize a diagram only to reuse one across N docs.
- **Read frontmatter first**, filter by `type`/`tags`/`status`, then read only the relevant bodies.
  The write only pays off on the read: knowledge stored but **not retrieved** (missed read), or
  retrieved but **not applied**, fails as if never written — the index exists so "didn't find it" is
  no excuse (mirrors the read-before-act rule in the canon's behavioral seed).
- **Agent-ignore** — an ignore list (`.aiignore`, or reuse `.gitignore`) so the agent skips attachments /
  archives / heavy binaries instead of full-scanning them into context; archive completed work **out** of active context.

## Discipline
- Contract-first: update the doc (schema/contract/behavior) **before** the code change, then code — and
  **sweep every occurrence of the contract in the same change** (no stragglers left pointing at the old shape).
- Keep docs consistent with the code; mark assumptions. A plan: `active/` → `done/` on completion (drop its backlog item).
- **Staleness is a first-class failure — a drifted doc actively misleads, worse than none.** Acting
  *from* a doc that contradicts the code you touch → reconcile the doc **as part of the task**, never
  proceed on the stale version (the read-side trap: retrieved, but no longer valid). `status:` carries
  validity — mark a superseded doc `archived` so the `read-frontmatter-first` pass filters it out; a
  stale doc still reading `active`/`in_progress` is a landmine for the next session.
- A decision records **why** (context, decision, alternatives, dead-ends), not only what.

[CRITICAL] Do not ship a schema / contract / behavior change without updating its doc first.
