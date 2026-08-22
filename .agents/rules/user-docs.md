---
name: user-docs
description: User-facing product documentation (README, wiki, API reference, usage, changelog). Apply when a change alters the product's external surface.
---

# user-docs

User-facing docs, separate from internal `project-docs`. Language: the user's / target
market's language (canon language policy), not English-by-default.

Audience here = **product users** (people using the product). Deliverables for OTHER non-dev
audiences — business / client / marketing / published content — are NOT user-docs; they live in
`content/` (see `content-vault`). Internal dev docs → `project-docs` (`docs/`); consumed inputs → `context/`.

## Locations
- `README` — the minimum: a brief description (what it is / how to run / how to use).
- A documentation folder for fuller product / API reference — its name **varies by project**
  (`docs/`, `wiki/`, `guide/`, …). Use whatever the project already has; do not impose a name.
- **Format: Markdown (`.md`) by default** — author README, wiki pages, and guides in md (readable,
  diffable, renders in git/wiki). Export/render only when the surface needs it (PDF via `md2pdf`,
  a generated API reference) — the Markdown stays the source. Don't hand-author HTML/PDF/docx.
- **How-to / walkthrough of a live UI** — capture it via `browser-use` (drive the real flow →
  `browser_take_screenshot` each step → narrate), don't fake steps from memory. Screenshots sit by the guide.

## Duplicate here ONLY on an external-surface change
- new feature / changed behavior / new-or-changed API or CLI usage / user-set config → update user-docs.
- internal refactor, bug fix with no surface change, architecture / infra → stays in
  `project-docs` (agent docs), **NOT** duplicated here.

## Lifecycle — working → final (the user doc is DISTILLED, not copied)
Working material (dev docs in `docs/`, plans, decisions, session notes) is **not** the user doc — it
answers "how it is built / why". The user doc answers "how do I do X". Promote by **distilling**, never
by copy-pasting dev prose:
- **Draft** — while shaping, mark `status: draft`.
- **Final** — rewritten for the user's task, in the user's language, without internal rationale or ADR
  reasoning; mark `status: published`. The single authoritative surface for users.
- **Archive** — superseded → `status: archived`, out of the active surface.

The dev source of truth stays in `project-docs` / `docs/decisions` and is **not** duplicated here — link
to it only if a user genuinely needs it.

## Discipline
- After a surface-changing change, correct or create the user doc in the same session.
- Depth scales by project (library → API reference; CLI → usage; app → README + guide;
  internal tool → README only, or none).
