// opencode docs-frontmatter plugin. Install into ./.opencode/plugin/docs-frontmatter.ts
// Non-blocking: warns when a doc under a docs/ tree is written without the project-docs
// frontmatter (type/status/tags/project). Never throws — a nudge, not a gate.
import type { Plugin } from "@opencode-ai/plugin"
import { readFileSync } from "node:fs"

const REQUIRED = ["type", "status", "tags", "project"]

export const DocsFrontmatter: Plugin = async () => ({
  "tool.execute.after": async (input, output) => {
    if (!["edit", "write"].includes(input.tool)) return
    const a = (output && (output as { args?: Record<string, unknown> }).args) || {}
    const p = (a.filePath || a.path) as string | undefined
    if (typeof p !== "string" || !p) return
    if (!/\.md$/.test(p) || p.includes("/.agents/")) return
    if (!/(^|\/)docs\//.test(p)) return
    let text = ""
    try { text = readFileSync(p, "utf8") } catch { return }
    const first = text.split(/\r?\n/).find((l) => l.trim() !== "") ?? ""
    if (first.trim() !== "---") {
      console.error(`docs-frontmatter: ${p} — no YAML frontmatter. Add ${REQUIRED.join("/")} (project-docs rule; model: task_center/docs).`)
      return
    }
    const m = text.match(/^---[ \t]*\r?\n([\s\S]*?)\r?\n---/)
    const block = m ? m[1] : ""
    const missing = REQUIRED.filter((k) => !new RegExp(`^${k}:`, "m").test(block))
    if (missing.length) console.error(`docs-frontmatter: ${p} — frontmatter missing key(s): ${missing.join(" ")} (project-docs).`)
  },
})
