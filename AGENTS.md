# spintax-dotnet — AGENTS.md

## Pointer (where to look, in priority order)

- Rules: first `./.agents/rules/`, then the library `~/.agents/rules/`.
- Skills/agents: first `./.agents/`, then the library.
- Links and MCP configs: first the local `./.agents/map.yaml` + `./.agents/mcp-configs.yaml`;
  `~/.agents/...` — only to deploy a new rule. The build snapshot — in
  `./.agents/generated/.agents.lock.yaml`.
- Adaptation registry: `./.agents/REGISTRY.md` — WHY something was added/changed (the WHAT graph
  lives in `map.yaml`, do not duplicate it).
- **[CRITICAL] Plans, docs, and work-artifacts live ONLY in this project** — `./.agents/plans/{active,done}`,
  `./docs/`, the project tree. NEVER write them to `~/.claude/`, `~/.codex/`, or any home/global
  agent folder, including Claude Code's own memory. Scratch/temp → the session scratchpad.
- On conflict the project wins (more specific overrides more general).

## What this is

The **fifth engine of the Spintax family**, for .NET: `src/Spintax.Core`, published as the NuGet
package `Spintax.Core`. It was built as the ZennoPoster port in `spintax-zenno` (M0–M1 there: the
C#-port decision on measurement, the corpus brought to 258/258) and extracted into its own
repository on 2026-08-22 so that the engine has the same standing as `@spintax/core`,
`spintax/core`, `spintax-core` and `spintax-win`. `spintax-zenno` now consumes it from a sibling
checkout (`../spintax-dotnet`) until a published package replaces that.

**The golden corpus is the source of truth, not the eye:**
`W:\projects\spintax-js\packages\conformance\fixtures\*.json` — read from a checkout, never
vendored (`docs/decisions/0001`). The reference implementation is `@spintax/core` (TypeScript);
the closest model is the Pascal port `spintax-win`. Second oracle: the local MCP `@spintax/mcp`
(`./.mcp.json`).

## Constraints (the package's contract, tested by `AssemblyContractTests`)

| Constraint | Requirement |
|---|---|
| Targets | `netstandard2.0;net472` from one source (`docs/decisions/0002`) |
| Dependencies | **zero** NuGet packages in the shipped dll — BCL only |
| Platform | AnyCPU |
| Threads | no mutable static state; `Random` is local, from a seed |
| Public API | no `Task<T>`; `string` / `IReadOnlyDictionary<string,string>` / small DTOs |
| Language | `LangVersion latest`, but no `record`, no `init`, no default interface members (netstandard2.0 lacks the runtime types) |

**Never publish an unmeasured number.** Corpus totals come from the runner; performance numbers
from the other ports are not transferable.

## Behavioral rules (base seed — expand as you work)

- **Think before coding.** State assumptions; if uncertain, ask. Present competing
  interpretations — don't pick silently. Push back when a simpler path exists.
- **Simplicity first.** Minimum code that solves the problem — no speculative features.
- **Surgical changes.** Touch only what the request needs; match existing style.
- **Goal-driven + verify.** Brief plan, per-step verification; confirm by an independent check,
  not assertion (see `proof-loop`, `code-review`).
- **Chat answers: structured and plain.** Lead with the answer, then the why.
- **Workspace hygiene — clean up when done.** Kill what you started, remove scratch files.
- **Don't block on a slow tool.** Proceed without it and say so.

Lessons carried over from `spintax-zenno` (each traces to a real incident):

- **Backslashes do not survive a bash heredoc here — write scripts to a file.** `\f` in a heredoc
  landed as a literal form feed; `\t` became a tab inside a C# comment. Anything with a backslash
  goes through the Write tool into a file that is then executed; never inline.
- **Non-ASCII in C# literals is escaped, always.** U+2028/U+2029 typed raw inside `'…'` are line
  terminators to the compiler (CS1010); combining marks render as garbage in a diff. Write
  `\uXXXX` from the start.
- **`Assert.DoesNotContain("\0", s)` is always red on .NET 5+.** The string overload is
  culture-sensitive and ICU treats NUL as ignorable. Use the `char` overload, or ordinal
  comparison, whenever an assertion involves a control character.
- **JS → .NET port rules bought by measurement:** never `\w \d \s \b` (Unicode classes differ),
  never `String.Trim()` (JS whitespace set; see `JsText`), JS multiline `^ $ .` see four line
  terminators, `\G` for sticky, JS `$` → `\z`, mulberry32 + FNV-1a pinned to Node outputs,
  plural counts as `double`, `#def` roll in `Object.keys` order, `JSON.stringify` escapes in
  messages.

## Self-configuration (adapt and explain)

1. Local in `./.agents/` — already there? use it.
2. No → in `~/.agents/`? pull the chain, append to the local `./.agents/map.yaml` and the pointer.
3. Nowhere → escalate, attach into the project, append to the local map.

Adapting the PROJECT is autonomous; changing the BASELINE `~/.agents` needs the user's agreement.
**[CRITICAL] Any attach/install/replace gets a line in `REGISTRY.md`.**

## Commands

Toolchain: **.NET SDK 8** (9 works too). The gates, in the order the git hooks run them
(`.agents/rules/quality-csharp.md`):

```
dotnet build Spintax.sln -nologo -v q                       # commit gate; warnings are errors
dotnet format Spintax.sln --verify-no-changes -v q          # push gate
dotnet test Spintax.sln --no-build -nologo -v q             # push gate; xunit on net8.0 and net472
dotnet run --project tests/Spintax.Corpus -f net8.0 --no-build -- --baseline tests/known-failures.txt
dotnet run --project tests/Spintax.Corpus -f net472 --no-build -- --baseline tests/known-failures.txt
                                                            # push gate; the acceptance suite, both hosts
dotnet pack src/Spintax.Core -c Release -o artifacts        # the package (RELEASING.md)
```

The corpus runner without `--baseline` exits 1 on any failure; with it, it is a regression gate.
The corpus resolves from `SPINTAX_FIXTURES`, then the sibling `../spintax-js`; the gate fails loudly
when neither does.

## Attached at initialization

- Library version: `059b4c7` (see `.agents/generated/.agents.lock.yaml`), chain copied from
  `spintax-zenno`'s adapted `.agents/` (see `REGISTRY.md`)
- Domains: coding (base always on top)
- Rules: 15 — base plus the coding chain, with `quality-csharp` authored in `spintax-zenno`
- Agents: `docs`, `searcher`, `reviewer` · Hooks: `secrets-guard`, `light-lint`,
  `docs-frontmatter`, `git-quality-gate`
- MCP: `spintax` (project-bound, `./.mcp.json`); `playwright` is global machine infra.
