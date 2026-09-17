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
`<spintax-js checkout>/packages/conformance/fixtures/*.json` — resolved from `SPINTAX_FIXTURES`,
then the sibling `../spintax-js`, and read from a checkout, never vendored (`docs/decisions/0001`).
On this machine that is `C:\projects\spintax\spintax-js`; the `W:\` path this line used to give
does not exist since the PC move. The reference implementation is `@spintax/core` (TypeScript);
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
- **…and a `\uXXXX` written with one backslash does not reach the file — the tooling resolves it
  in transit.** Three times in one session (2026-09-17), this bullet included: a written
  `CharClass` landed with a raw U+2028 and would not compile (CS1010); a char literal for
  U+017F / U+212A landed as the raw letters, which COMPILES and reads as an ASCII `K` in every
  diff; and the first draft of this line lost its own examples the same way. What survives the
  trip: TWO backslashes inside a regex pattern string — the C# compiler turns them back into one
  and the regex engine reads the escape — and `\xNNNN` with all four hex digits inside a char or
  string literal, which is what `IsEmailLocalChar` and the NBSP capitalizer test use now. In
  prose, name the code point (U+017F), never the escape. After writing a file with non-ASCII
  intent, check it: `grep -nP '[^\x00-\x7F]'` and read the lines it prints.
- **Warm the JIT before quoting a .NET number, and never quote a dialect claim you did not run.**
  The first post-process probe read 1216 ms per 1000 prose renders against a 33 ms baseline, and
  that "37× regression" was the first block paying JIT for the whole engine — warm, it is 26 ms
  against 24. In the same session a comment asserted how `RegexOptions.IgnoreCase` folds on each
  host; measured, it folds differently than the comment said. A probe runs a warm-up first, and a
  sentence about a dialect gets a two-minute test before it gets committed.
- **`Assert.DoesNotContain("\0", s)` is always red on .NET 5+.** The string overload is
  culture-sensitive and ICU treats NUL as ignorable. Use the `char` overload, or ordinal
  comparison, whenever an assertion involves a control character.
- **JS → .NET port rules bought by measurement:** never `\w \d \s \b` (Unicode classes differ),
  never `String.Trim()` (JS whitespace set; see `JsText`), JS multiline `^ $ .` see four line
  terminators, `\G` for sticky, JS `$` → `\z`, mulberry32 + FNV-1a pinned to Node outputs,
  plural counts as `double`, `#def` roll in `Object.keys` order, `JSON.stringify` escapes in
  messages.
- **Do not write a contract stronger than the code can hold.** "The count is exact or it saturates,
  never understating" was added to `Census`, `Engine` and the README as a conclusion, not a measured
  fact — and five review rounds then found five different templates that falsified it, each costing
  a fix that created the next one. A static walk describing a dynamic engine parts company with it
  at every exhaustion path. Claim what is pinned by a test, saturate where the walk cannot bound an
  answer, and put the rest in `docs/TODO.md` as a measured gap. A documented gap is cheap; a
  documented guarantee that is false is a defect in every reader's plans.
- **A render change is not done until `Census` makes the same change.** `Combinations` and
  `MaxLength` describe the walk that `Render` performs, so a semantic fix to one silently makes the
  other lie — and the corpus cannot see it, because no fixture asserts a count. The splice fix
  (#1) shipped the renderer first and left `MaxLength` reporting 5 for a template whose longest
  render is 7: understating a length is the half a host acts on. Change both, and pin the pair with
  an exhaustive-enumeration test.

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
