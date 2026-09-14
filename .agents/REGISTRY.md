# REGISTRY — adaptation log

WHY something was attached or changed. The WHAT graph lives in `map.yaml`; do not duplicate it
here.

## 2026-09-14 — хуки запускаются переносимо, без абсолютного пути к bash

Команды хуков в `.claude/settings.json`, `.codex/config.toml` и фрагментах `.agents/hooks/*/{claude.json,codex.toml}`
переписаны на единый запуск:
`git -c "alias.agent-hook=!bash .agents/hooks/<hook>/<script>.sh" agent-hook [--claude|--codex]; exit $LASTEXITCODE`.

Почему. Прежние формы ломались на Windows: абсолютный `W:\Program Files\Git\bin\bash.exe` не существует на другом ПК,
а голый `bash` из PowerShell попадает в WSL-лаунчер. Claude Code (когда Git стоит не в стандартном месте) и Codex
запускают хуки через PowerShell. Проверено: `!`-алиас git исполняется собственным sh Git-а (bash = Git Bash), из корня
репозитория; stdin/stdout/stderr и код выхода проходят. `; exit $LASTEXITCODE` нужен, потому что `powershell -Command`
превращает exit 2 в 1, а для Claude это неблокирующая ошибка: **secrets-guard на этом ПК молча пропускал `.env`**
(красный тест на старом конфиге, зелёный на новом — Claude в режимах PowerShell и Git Bash, Codex). В sh переменная
пуста — обычный `exit` с кодом git. Требование: проект — git-репозиторий, `git` в PATH.

Заменяет `.agents/hooks/bash.cmd` (только Windows) из записи ниже; файл удалён.

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
