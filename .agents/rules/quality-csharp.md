---
name: quality-csharp
description: Quality gate for C# / .NET in this project (netstandard2.0 library for ZennoPoster + net8.0 test hosts). Apply when editing .cs/.csproj/.props.
---

# quality-csharp

Authored locally in spintax-zenno when the C# toolchain was fixed (no C# rule in the `~/.agents` baseline; precedent:
`quality-pascal` in `spintax-win`). The compiler is the linter, `dotnet format` is the
formatter, the golden corpus is the acceptance suite.

Run each check at its moment:

- after any change → `dotnet build Spintax.sln -nologo -v q` — warnings are errors
  (`Directory.Build.props`), so a clean build is the lint
- before marking a task done → `dotnet format Spintax.sln --verify-no-changes` and
  `dotnet test Spintax.sln`
- before a push → the corpus on both hosts: `dotnet run --project tests/Spintax.Corpus -f net8.0
  -- --baseline tests/known-failures.txt` and the same with `-f net472` (the runtime class
  ZennoPoster loads the dll into; the BCL's Unicode tables are what can differ). It is **not**
  skip-if-missing — no corpus ⇒ exit 2, and the push is blocked (ADR-0001). A case that starts
  passing must be removed from `tests/known-failures.txt` in the same commit; a new failure is
  a regression, never a baseline entry.

The git hook (`.agents/hooks/git-quality-gate/gate.sh`) runs exactly these: build on
commit; build + format + test + corpus on push. A missing SDK **blocks** — this repository's
product is the dll, so there is nothing to skip to.

Baseline tools — do not silently swap:

- build / analyzers → `dotnet build` with `TreatWarningsAsErrors`
- format → `dotnet format` (bundled with the SDK), settings in `.editorconfig`
- tests → xunit in `tests/Spintax.Core.Tests` (net8.0 and net472)
- acceptance → `tests/Spintax.Corpus` (console, net8.0 and net472), reads the corpus from a checkout

Constraints the shipped dll must keep (the package contract, `AGENTS.md`) — checked by
`tests/Spintax.Core.Tests/AssemblyContractTests.cs` against the built assembly, not by eye:

- `netstandard2.0;net472` from one source, **zero** package references, AnyCPU, no `Task<T>` in the public surface,
  no mutable static state (static `readonly` of an immutable type is fine — `Regex`, `string`,
  primitives).
- `LangVersion` is `latest` for everyone; on `netstandard2.0` that means **no `record`, no
  `init`, no default interface members** — they need runtime types the target lacks
  (`IsExternalInit`; measured: CS0518). Plain classes with constructor-set, get-only
  properties instead.
- The zero-NuGet rule is about `src/Spintax.Core` only; test hosts may reference packages.

Port-specific traps (measured, see `docs/TODO.md` "Правила порта"):

- never `\w`, `\d`, `\s`, `\b` in a regex and never `String.Trim()` on engine text — JS and .NET
  disagree on Cyrillic (`\w`), on U+FEFF / U+0085 (`\s`, `trim`). Spell the class or the set.
- sources are UTF-8 **without BOM** (`.editorconfig`). Do not set `<CodePage>`: Roslyn detects
  BOM-less UTF-8 itself, and `CodePage=65001` makes `dotnet format` report a false `CHARSET`
  error on every file.

There is no `light-lint` for `.cs`: a single file cannot be linted outside its project in
useful time. The build on commit is the check.
