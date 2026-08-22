---
name: quality-cpp
description: Quality gate for C++. Apply when editing C++ in a project that uses it.
---

# quality-cpp

After editing C++:

- compile with warnings → the project's build first; else the environment's compiler:
  `g++`/`clang++ -Wall -Wextra -c <file>` (POSIX), MSVC `cl /W4 /c <file>` (Windows without g++/clang)
- `clang-tidy <file>` — lint (if installed)
- `clang-format <file>` — format (if installed)

Baseline tools:

- compiler → the project's toolchain; standalone default g++/clang++ (`-Wall -Wextra`), MSVC (`/W4`)
  where that's what the machine has
- lint → clang-tidy — install when needed (not in the base system)
- format → clang-format — install when needed

Installing clang-tidy / clang-format is a project decision (project principle: install
when needed) — record it in `./.agents/REGISTRY.md`.
