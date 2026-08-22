# Spintax.Core (.NET)

[![NuGet](https://img.shields.io/nuget/v/Spintax.Core.svg)](https://www.nuget.org/packages/Spintax.Core)
[![CI](https://github.com/investblog/spintax-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/investblog/spintax-dotnet/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A **[Spintax](https://spintax.net) engine** for .NET — parse, render, validate, extract, analyze
and neutralize spintax templates. MIT, zero dependencies, `netstandard2.0` and `net472` from one
source: it runs on .NET Framework 4.7.2+ and on every .NET since.

This is the fifth engine in the Spintax family, and an **independent implementation** — not a
transcription of the others. It is held to the same behaviour contract by a **shared golden
corpus** of language-neutral fixtures, the one that gates the TypeScript, PHP, Python and Object
Pascal engines. All **258 of its cases pass here, on both targets, none skipped, none expected to
fail** — the 254 deterministic ones on output, the 4 `kind:rng` ones through the engine's RNG seam.

## Install

```
dotnet add package Spintax.Core
```

## Use

```csharp
using System.Collections.Generic;
using Spintax.Core;

Engine.Render("{Hello|Hi} there!");                                   // "Hello there!" or "Hi there!"
Engine.Render("{Hello|Hi} there!", new RenderOptions { Seed = "42" }); // same seed, same output, every time
Engine.Render("Hi %name%!", new RenderOptions
{
    Context = new Dictionary<string, string> { ["name"] = "Sam" }      // "Hi Sam!"
});

// Check a template before you ship it: diagnostics carry a stable code and a 1-based position.
foreach (var d in Engine.Validate("{a|b"))
    System.Console.WriteLine($"{d.Severity} {d.Line}:{d.Column} [{d.Code}] {d.Message}");
// Error 1:1 [bracket.unclosed] Unclosed '{'.

// How much a template can say — counted by walking the tree, not sampled.
Engine.Combinations("{a|b} {c|d|e}");                                  // 6
```

Rendering is **lenient**: malformed markup degrades rather than throwing, so a template a
non-programmer wrote cannot take a page down. Output is **tidied up** by default — sentence
capitalisation, spacing around punctuation, URLs and abbreviations left intact; pass
`PostProcess = false` to get the raw pick.

Values in `Context` are **re-parsed as templates** when they contain `{`, `[` or `%` — the
reference engine does the same, so a host can pass spintax through a variable on purpose. Data
from a table or a scraped page must go through `Engine.Neutralize(value)` first: it shields the
structural characters so that `Aurix {Mini|Maxi}` prints as written instead of spinning.

Syntax — enumerations `{a|b}`, permutations `[<minsize=2;sep=", ">a|b|c]`, variables `%name%`,
conditionals `{?VAR?yes|no}`, plural agreement `{plural %n%: one|few|many}`, comments `/# … #/`,
and the `#set` / `#def` / `#include` directives — is documented in full at
**[spintax.net/docs](https://spintax.net/docs/)**.

## Public API

```csharp
static class Engine
{
    string                    Render(string template, RenderOptions? options = null);
    IReadOnlyList<Diagnostic> Validate(string template, ValidateOptions? options = null);
    ExtractResult             Extract(string template);      // variables referenced and defined, includes
    Analysis                  Analyze(string template, ValidateOptions? options = null);
    string                    Neutralize(string value);
    long                      Combinations(string template, IReadOnlyDictionary<string,string>? vars = null, string? locale = null);
    long                      MaxLength(string template, IReadOnlyDictionary<string,string>? vars = null, string? locale = null);
}
class RenderOptions   { Context; Seed; Locale; IncludeResolver; PostProcess = true; MaxDepth; OnPluralError; }
class ValidateOptions { Locale; KnownIncludes; KnownVariables; }
```

Flat signatures on purpose — strings, dictionaries, small DTOs, no `Task<T>` — so the engine
drops into hosts that load a dll by name and compile snippets against it — ZennoPoster was the
first, and its facade, project templates and measurements ship with that plugin. No mutable
static state: dozens of threads can
render with the same seed and get the same bytes.

`Combinations` counts **choice paths** — the number of distinct texts when no two options spell
the same thing (`{a|a}` is 2). With `vars` it is the count for that one row of data (a conditional
takes its branch, a plural its form); without, across every state of the data.

## The family

- **TypeScript / JavaScript:** [`@spintax/core`](https://www.npmjs.com/package/@spintax/core)
  ([source](https://github.com/investblog/spintax-js)) — the reference engine, and the home of the
  golden corpus, the MCP server and the n8n node.
- **PHP:** [`spintax/core`](https://packagist.org/packages/spintax/core)
  ([source](https://github.com/investblog/spintax-php)).
- **Python:** [`spintax-core`](https://pypi.org/project/spintax-core/)
  ([source](https://github.com/investblog/spintax-py)).
- **Object Pascal:** [spintax-win](https://github.com/investblog/spintax-win) — the engine inside
  [Spintax Studio](https://spintax.studio).
- **WordPress:** [the original plugin](https://github.com/investblog/spintax) — the origin engine, GPL.

The engines are independent implementations held together by the shared corpus, not ports of one
another's code. A template written for one renders the same on every other; a seed reproduces a
draw **within** an engine, not across them (the deterministic fixtures use an injected RNG
strategy, so the contract does not depend on it).

## Why another one

The .NET packages named for spintax implement the flat `{a|b}` dialect of the 2010s. This engine
implements the spintax.net superset — permutations that pick and order a subset, scoped
variables, value-driven conditionals, locale-aware plural agreement for the three-form languages
(ru, uk, be, sr, hr, bs) as well as the two-form default, includes, and a post-processing pass —
with the diagnostics and the determinism a pipeline needs, and it is tested against the same
corpus as four other implementations rather than against its own expectations.

## Conformance

`tests/Spintax.Corpus` loads the shared golden corpus (the exact JSON fixtures the other engines
consume) and asserts every case. `tests/known-failures.txt` is a baseline in the Pascal port's
sense: a new failure fails the build, and a case that starts passing must be removed from the file
in the same commit. It is empty.

```
PASS=258  FAIL=0  SKIP=0    # net8.0 and net472, 2026-08-22
```

The port's own unit tests (`tests/Spintax.Core.Tests`, 157 on each host) pin what the corpus
cannot express: 1-based positions, plural buckets for locales the corpus lacks, JavaScript text
semantics (`JsText`: the JS whitespace set, the four line terminators, `String.prototype.toUpperCase`
special casing), the exact census, and the assembly contract — two targets, zero package
references, AnyCPU, no mutable static state, no `Task<T>` on the surface.

## Build and test

The full gate needs **Windows with the .NET SDK 8** — the `net472` test host and the `net472`
corpus run need the .NET Framework runtime, and the library deliberately adds no
reference-assemblies package. On Linux or macOS build and test the `net8.0` host only
(`dotnet test -f net8.0`, `dotnet run … -f net8.0`); the `netstandard2.0` library builds anywhere.

```sh
git clone https://github.com/investblog/spintax-js ../spintax-js     # the corpus, once
dotnet build Spintax.sln -nologo -v q                                # warnings are errors
dotnet test Spintax.sln --no-build -nologo -v q                      # net8.0 and net472
dotnet run --project tests/Spintax.Corpus -f net8.0 --no-build -- --baseline tests/known-failures.txt
dotnet run --project tests/Spintax.Corpus -f net472 --no-build -- --baseline tests/known-failures.txt
```

The corpus is read from `SPINTAX_FIXTURES` or the sibling checkout — never vendored
(`docs/decisions/0001`). Without it the runner **fails** (exit 2) rather than passing an empty
run. Why two targets: `docs/decisions/0002` — a .NET Framework host that compiles snippets
against the dll has no `netstandard.dll`, so the `net472` build is the one that ships there.

## License

[MIT](LICENSE). The WordPress plugin remains GPL; MIT/Expat is GPL-compatible.

---

Part of the [301.st](https://301.st) toolset. Product home: [spintax.net](https://spintax.net).
