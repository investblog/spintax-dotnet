---
type: decision
status: accepted
tags: [build, netstandard, net472, zennoposter]
project: spintax-dotnet
---

# 0002 — `netstandard2.0;net472` from one source

**Date:** 2026-08-22 (measured in spintax-zenno on ZennoPoster Pro 7.9.1.0).

## Context

The engine was born as a `netstandard2.0` library for ZennoPoster. On the live product the dll
loaded, `Version` answered — and the first cube that touched `Dictionary<,>` from its API failed to
compile: `CS0012: The type 'Object' is defined in an assembly that is not referenced. You must add
a reference to assembly 'netstandard, Version=2.0.0.0'`. The cube compiler references `mscorlib`,
not `netstandard.dll`; asking every user to add a `netstandard` reference by hand is not a product.

## Decision

The library multi-targets `netstandard2.0;net472` from the same source. The `net472` build
references `mscorlib`, `System`, `System.Core` and `System.Numerics` directly and is what ships
to .NET Framework hosts; `netstandard2.0` serves everything else. The assembly contract tests
accept either target, and the unit tests and the corpus run on both hosts, so the build that ships
is the build under test.
