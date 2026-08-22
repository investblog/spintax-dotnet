#!/usr/bin/env bash
# docs-frontmatter — PostToolUse hook: NON-BLOCKING check that a doc written under a
# `docs/` tree carries the project-docs frontmatter (type/status/tags/project). Reads the
# tool-call JSON on stdin, inspects changed `docs/**.md` files, warns on stderr, ALWAYS
# exits 0 (a nudge, not a gate — "a rule asks, a hook guarantees" only for hard invariants;
# doc metadata is advisory). Model to copy: task_center/docs (frontmatter on every doc + ADR).
# Python absent -> silent no-op (advisory hook); resolve AGENTS_PYTHON/python3/python/py -3.
[ -n "${AGENTS_DEBUG:-}" ] && set -x   # opt-in trace: AGENTS_DEBUG=1
run_python() {
  if [ -n "${AGENTS_PYTHON:-}" ]; then "$AGENTS_PYTHON" "$@"; return $?; fi
  if command -v python3 >/dev/null 2>&1; then python3 "$@"; return $?; fi
  if command -v python >/dev/null 2>&1; then python "$@"; return $?; fi
  if command -v py >/dev/null 2>&1; then py -3 "$@"; return $?; fi
  return 127
}

check_one() {
  f="$1"
  [ -f "$f" ] || return 0
  # only markdown inside a docs/ tree; never the copied library layer
  case "$f" in
    */.agents/*) return 0 ;;
    docs/*.md|*/docs/*.md) : ;;
    *) return 0 ;;
  esac
  # first non-blank line must open a YAML frontmatter block
  first=$(grep -m1 -v '^[[:space:]]*$' "$f" 2>/dev/null)
  if [ "$first" != "---" ]; then
    echo "docs-frontmatter: $f — no YAML frontmatter. Add type/status/tags/project so docs are filterable by metadata (project-docs rule; model: task_center/docs)." >&2
    return 0
  fi
  # extract the block between the first two --- fences and check the required keys
  block=$(awk '/^---[[:space:]]*$/{ if(!b){b=1;next} else exit } b{print}' "$f")
  missing=""
  for k in type status tags project; do
    printf '%s\n' "$block" | grep -qE "^${k}:" || missing="$missing $k"
  done
  [ -n "$missing" ] && echo "docs-frontmatter: $f — frontmatter missing key(s):$missing (project-docs: type/status/tags/project)." >&2
  return 0
}

while IFS= read -r f; do
  f=${f%$'\r'}   # Windows Python writes CRLF to the pipe; strip the CR or [ -f ] silently misses
  [ -n "$f" ] && check_one "$f"
done < <(run_python -c '
import sys,json,re
try:
    d=json.load(sys.stdin)
except Exception:
    sys.exit(0)
ti=d.get("tool_input") or d.get("input") or {}
ti=ti if isinstance(ti,dict) else {}
ps=[]
for k in ("file_path","path","filePath"):
    v=ti.get(k)
    if isinstance(v,str) and v: ps.append(v)
patch=ti.get("patch")
if isinstance(patch,str):
    ps+=re.findall(r"(?m)^\*\*\* (?:Update|Add) File: (.+)$", patch)
    ps+=re.findall(r"(?m)^\+\+\+ b/(.+)$", patch)
print("\n".join(dict.fromkeys(ps)))
' 2>/dev/null)
exit 0
