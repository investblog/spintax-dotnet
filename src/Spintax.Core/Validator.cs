using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Spintax.Core
{
    /// <summary>
    /// Port of <c>internal/validator.ts</c> — the static validator (parity gate §3.1). Produces
    /// <see cref="Diagnostic"/>s with the canonical codes of the corpus README; "valid" ⇔ no
    /// <see cref="Severity.Error"/>. Raw-text scans, like the plugin's validator: that is what
    /// lets bracket imbalance and constructs nested inside <c>[…]</c> be seen. Positions are
    /// 1-based and best-effort — the corpus gates code and severity, the repair loop of M3
    /// relies on the positions.
    /// </summary>
    internal static class Validator
    {
        private const string Word = "[A-Za-z0-9_]";
        private const string B = @"(?:(?<![A-Za-z0-9_])(?=[A-Za-z0-9_])|(?<=[A-Za-z0-9_])(?![A-Za-z0-9_]))";
        private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex VariableRe = new Regex("%(" + Word + "+)%");
        private static readonly Regex ConditionalRefRe = new Regex(@"\{\?!?([A-Za-z_]" + Word + @"*)\?");
        private static readonly Regex IncludeRe = new Regex(
            JsText.LineStart + @"[ \t]*#include[ \t\n\r\f\x0B]+""([^""]+)""[ \t\n\r\f\x0B]*" + JsText.LineEnd);
        private static readonly Regex IncludeWordRe = new Regex("#include" + B);
        private static readonly Regex ConfigPrefixRe = new Regex(@"\[<([^>]*?)>");
        private static readonly Regex AnyKeyEqualsRe = new Regex(Word + "+" + JsText.S + "*=");
        private static readonly Regex KeyEqualsRe = new Regex("(" + Word + "+)" + JsText.S + "*=");
        private static readonly Regex MinSizeRe = new Regex("minsize" + JsText.S + "*=" + JsText.S + "*([^;>" + JsText.SChars + "]+)", Ci);
        private static readonly Regex MaxSizeRe = new Regex("maxsize" + JsText.S + "*=" + JsText.S + "*([^;>" + JsText.SChars + "]+)", Ci);
        private static readonly Regex DigitsRe = new Regex(@"^[0-9]+\z");
        // Spintax still unresolved when plural agreement runs: `[`, or `{` that does not open a
        // conditional — conditionals resolve BEFORE plurals, enumerations and permutations after.
        private static readonly Regex UnresolvedAtPluralTimeRe = new Regex(@"\[|\{(?!\?)");
        // Any bracket at all — construct-free is the SUFFICIENT condition this validator proves.
        private static readonly Regex AnyBracketRe = new Regex(@"[\[\]{}]");
        private static readonly Regex ConditionalOpenRe = new Regex(@"\{\?");
        private static readonly Regex DefinitionLineRe = new Regex(
            JsText.LineStart + @"[ \t]*#(?:set|def)[ \t]+%(" + Word + @"+)%[ \t]*=[ \t]*(" + JsText.Dot + "*?)" + JsText.LineEnd);
        private static readonly Regex NonNewlineRe = new Regex("[^\n]");

        private const int FormExpansionPasses = 51;
        private const int FormExpansionMaxGrowth = 64 * 1024;
        private const int CyclePathLimit = 8;

        public static List<Diagnostic> ValidateTemplate(string src, ValidateOptions? options)
        {
            var o = options ?? new ValidateOptions();
            var diagnostics = new List<Diagnostic>();
            var text = Parser.StripComments(src);
            var idx = new LineIndex(text);
            var directives = Parser.ExtractDirectives(text);

            CheckBrackets(text, diagnostics);
            CheckDirectives(text, directives, diagnostics);
            CheckPermutationConfigs(text, idx, diagnostics);
            CheckPlurals(text, idx, o.Locale, o.KnownVariables, directives, diagnostics);
            CheckVariableReferences(text, idx, o.KnownVariables, diagnostics);
            if (o.KnownIncludes != null && o.KnownIncludes.Count > 0)
                CheckIncludeTargets(text, idx, o.KnownIncludes, diagnostics);

            return diagnostics;
        }

        // ── positions ────────────────────────────────────────────────────────────────────

        private readonly struct Pos
        {
            public Pos(int line, int column, int? endLine = null, int? endColumn = null)
            {
                Line = line;
                Column = column;
                EndLine = endLine;
                EndColumn = endColumn;
            }

            public int Line { get; }

            public int Column { get; }

            public int? EndLine { get; }

            public int? EndColumn { get; }
        }

        private static Diagnostic Err(string code, string message, Pos p, IReadOnlyDictionary<string, object?>? data = null) =>
            new Diagnostic(Severity.Error, code, message, p.Line, p.Column, p.EndLine, p.EndColumn, data);

        private static Diagnostic Warn(string code, string message, Pos p, IReadOnlyDictionary<string, object?>? data = null) =>
            new Diagnostic(Severity.Warning, code, message, p.Line, p.Column, p.EndLine, p.EndColumn, data);

        private static IReadOnlyDictionary<string, object?> Data(params (string key, object? value)[] pairs)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        /// <summary>Line-start offsets, built once per call; every position is then a binary search.</summary>
        private sealed class LineIndex
        {
            private readonly List<int> _starts = new List<int> { 0 };
            private readonly int _length;

            public LineIndex(string text)
            {
                for (var i = 0; i < text.Length; i++)
                    if (text[i] == '\n') _starts.Add(i + 1);
                _length = text.Length;
            }

            /// <summary>1-based (line, column) of an offset, in UTF-16 units like the reference.</summary>
            public (int line, int column) At(int offset)
            {
                var end = Math.Min(Math.Max(offset, 0), _length);
                var lo = 0;
                var hi = _starts.Count - 1;
                while (lo < hi)
                {
                    var mid = (lo + hi + 1) >> 1;
                    if (_starts[mid] <= end) lo = mid;
                    else hi = mid - 1;
                }
                return (lo + 1, end - _starts[lo] + 1);
            }

            public Pos Span(int offset, int length)
            {
                var (sl, sc) = At(offset);
                var (el, ec) = At(offset + length);
                return new Pos(sl, sc, el, ec);
            }
        }

        // ── checks ───────────────────────────────────────────────────────────────────────

        /// <summary>Balanced <c>{}</c>/<c>[]</c> with proper nesting — a raw scan by code point, columns counted as the reference counts them.</summary>
        private static void CheckBrackets(string text, List<Diagnostic> output)
        {
            var stack = new Stack<(char ch, char expect, int line, int column)>();
            var line = 1;
            var column = 1;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++; // one code point, one column — `for (const ch of text)`
                    column++;
                    continue;
                }
                if (ch == '\n')
                {
                    line++;
                    column = 1;
                    continue;
                }
                if (ch == '{' || ch == '[')
                {
                    stack.Push((ch, ch == '{' ? '}' : ']', line, column));
                }
                else if (ch == '}' || ch == ']')
                {
                    if (stack.Count == 0)
                    {
                        output.Add(Err("bracket.unexpected-closing", $"Unexpected closing '{ch}'.",
                            new Pos(line, column, line, column + 1), Data(("bracket", ch.ToString()))));
                    }
                    else
                    {
                        var top = stack.Pop();
                        if (top.expect != ch)
                            output.Add(Err("bracket.mismatched", $"'{top.ch}' closed by '{ch}'.",
                                new Pos(line, column, line, column + 1), Data(("open", top.ch.ToString()), ("close", ch.ToString()))));
                    }
                }
                column++;
            }

            // Unclosed openers, innermost first — the reference iterates its array from the bottom.
            var unclosed = stack.ToArray();
            Array.Reverse(unclosed);
            foreach (var u in unclosed)
                output.Add(Err("bracket.unclosed", $"Unclosed '{u.ch}'.",
                    new Pos(u.line, u.column, u.line, u.column + 1), Data(("bracket", u.ch.ToString()))));
        }

        /// <summary>Directive lines must match the shared grammar; each name once; no <c>#include</c> in a <c>#def</c> value.</summary>
        private static void CheckDirectives(string text, ExtractedDirectives directives, List<Diagnostic> output)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var lineText = lines[i];
                var trimmed = lineText.TrimStart(' ', '\t');
                string? kind = null;
                foreach (var candidate in new[] { "#set", "#def" })
                {
                    if (trimmed.StartsWith(candidate + " ", StringComparison.Ordinal)
                        || trimmed.StartsWith(candidate + "\t", StringComparison.Ordinal))
                    {
                        kind = candidate;
                        break;
                    }
                }
                if (kind is null) continue;

                if (!Parser.DirectiveRe.IsMatch(trimmed))
                {
                    var column = lineText.Length - trimmed.Length + 1; // first non-space char
                    var line = i + 1;
                    var code = kind == "#def" ? "def.malformed" : "set.malformed";
                    output.Add(Err(code, $"Malformed {kind}. Expected: {kind} %name% = value",
                        new Pos(line, column, line, lineText.Length + 1)));
                }
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var occurrence in directives.Occurrences)
            {
                // A name defined twice is an error whichever directives are involved — the maps
                // flatten a collision to last-wins before anyone can see it.
                if (seen.TryGetValue(occurrence.Name, out var first))
                {
                    output.Add(Err("definition.duplicate-name",
                        $"Variable '{occurrence.Name}' is defined more than once (first on line {first}). A name belongs to one directive, once.",
                        new Pos(occurrence.Line, 1)));
                }
                else seen[occurrence.Name] = occurrence.Line;

                // Includes resolve after a definition is frozen, so one cannot be rolled into a value.
                if (occurrence.Kind == "def" && IncludeWordRe.IsMatch(occurrence.Value))
                {
                    output.Add(Err("def.include-in-value",
                        $"#include cannot appear in a #def value ('{occurrence.Name}'): includes resolve after the value is frozen. Use #set, or put the #include in the body.",
                        new Pos(occurrence.Line, 1)));
                }
            }
        }

        /// <summary><c>[&lt;config&gt;]</c> prefixes: known keys only; minsize/maxsize must be digit runs.</summary>
        private static void CheckPermutationConfigs(string text, LineIndex idx, List<Diagnostic> output)
        {
            foreach (Match m in ConfigPrefixRe.Matches(text))
            {
                var configStr = m.Groups[1].Value;
                if (!AnyKeyEqualsRe.IsMatch(configStr)) continue; // not a key=value config
                var configBase = m.Index + 2; // offset of configStr in text (past "[<")

                foreach (Match km in KeyEqualsRe.Matches(configStr))
                {
                    var rawKey = km.Groups[1].Value;
                    switch (rawKey.ToLowerInvariant())
                    {
                        case "minsize":
                        case "maxsize":
                        case "sep":
                        case "lastsep":
                            break;
                        default:
                            output.Add(Err("permutation.unknown-key", $"Unknown permutation config key: '{rawKey}'.",
                                idx.Span(configBase + km.Index, rawKey.Length), Data(("key", rawKey))));
                            break;
                    }
                }
                var min = MinSizeRe.Match(configStr);
                if (min.Success && !DigitsRe.IsMatch(min.Groups[1].Value))
                {
                    output.Add(Err("permutation.minsize-not-integer", $"minsize must be a positive integer, got '{min.Groups[1].Value}'.",
                        idx.Span(configBase + min.Index, min.Length), Data(("value", min.Groups[1].Value))));
                }
                var max = MaxSizeRe.Match(configStr);
                if (max.Success && !DigitsRe.IsMatch(max.Groups[1].Value))
                {
                    output.Add(Err("permutation.maxsize-not-integer", $"maxsize must be a positive integer, got '{max.Groups[1].Value}'.",
                        idx.Span(configBase + max.Index, max.Length), Data(("value", max.Groups[1].Value))));
                }
            }
        }

        /// <summary><c>{plural …}</c>: macro in the count, nested brackets in forms, form count vs locale arity.</summary>
        private static void CheckPlurals(string text, LineIndex idx, string? locale, IReadOnlyList<string>? knownVariables,
            ExtractedDirectives directives, List<Diagnostic> output)
        {
            // Guard on the NORMALISED base: a non-empty locale that normalises to '' skips the arity check.
            var baseLang = !string.IsNullOrEmpty(locale) ? Plurals.NormalizeBaseLang(locale) : "";
            var arity = baseLang.Length > 0 ? Plurals.PluralArity(baseLang) : 0;

            var tainted = MacroTaintedNames(directives.SetDefs);
            var macros = directives.SetDefs;
            var defs = directives.DefDefs;
            var hostNames = LowerSet(knownVariables);

            foreach (var block in Plurals.FindPluralBlocks(text))
            {
                var at = idx.Span(block.Start, block.End - block.Start);

                // A macro in the count slot is still unresolved spintax when the plural is decided.
                foreach (Match m in VariableRe.Matches(block.CountSlot))
                {
                    var name = m.Groups[1].Value.ToLowerInvariant();
                    if (!tainted.Contains(name)) continue;
                    output.Add(Err("plural.count-macro",
                        $"{{plural ...}}: the count '{m.Groups[1].Value}' is a #set macro, so it is still unresolved spintax when the plural is decided and the block renders empty. Define it with #def instead.",
                        at));
                }

                if (AnyBracketRe.IsMatch(block.FormsRaw))
                {
                    output.Add(Err("plural.nested-brackets", NestedBracketsMessage, at));
                    continue;
                }

                // The form list AS THE RENDERER WILL SEE IT (spintax-js#66).
                var expanded = ExpandFormsForCounting(block.FormsRaw, defs, macros, hostNames);
                if (expanded.directMacroSpintax)
                {
                    output.Add(Err("plural.nested-brackets", NestedBracketsMessage, at));
                    continue;
                }
                if (expanded.unresolved) continue;

                var count = expanded.forms;
                if (arity > 0)
                {
                    if (count != arity)
                        output.Add(Err("plural.arity", $"{{plural ...}}: expected {arity} forms, got {count}.",
                            at, Data(("expected", arity), ("got", count))));
                }
                else if (count != Plurals.DefaultPluralArity)
                {
                    // No locale ⇒ no arity VERDICT, but render defaults to 2 forms and would ship
                    // the fullwidth fallback — a warning says the one true thing (spintax-js#65).
                    var unusable = !string.IsNullOrEmpty(locale);
                    var d = Plurals.DefaultPluralArity;
                    var message = unusable
                        ? $"{{plural ...}}: {count} forms, and the locale '{locale}' does not normalize to a language the engine knows, so render falls back to {d} forms and leaves this block unresolved — pass a base tag such as 'en' or 'ru'."
                        : $"{{plural ...}}: {count} forms, but no locale was supplied. render defaults to {d} forms and leaves this block unresolved — pass the locale you will render with.";
                    var data = unusable
                        ? Data(("got", count), ("defaultArity", d), ("locale", locale))
                        : Data(("got", count), ("defaultArity", d));
                    output.Add(Warn("plural.locale-missing", message, at, data));
                }
            }
        }

        private const string NestedBracketsMessage =
            "{plural ...}: forms must not contain nested spintax brackets. Extract via #def first — a #set is substituted verbatim and would put the brackets straight back.";

        /// <summary>
        /// How many forms the plural stage will actually receive — or an admission that it is not
        /// knowable. A value is counted only when its form count is the same WHATEVER the roll
        /// does: no construct at all. Any host-supplied name, undefined reference, conditional
        /// on the macro path, or chain past the budget suppresses the count-based verdicts.
        /// <c>directMacroSpintax</c>: a <c>#set</c> reached without crossing a <c>#def</c>
        /// arrives verbatim and is still spintax when the plural is decided.
        /// </summary>
        private static (int forms, bool unresolved, bool directMacroSpintax) ExpandFormsForCounting(
            string formsRaw, IReadOnlyDictionary<string, string> defs, IReadOnlyDictionary<string, string> macros,
            HashSet<string> hostNames)
        {
            // Which brackets reach the form slot VERBATIM: a pre-order depth-first walk of the
            // #set chain in source order, the FIRST non-clean answer wins. Iterative.
            var seenMacro = new HashSet<string>(StringComparer.Ordinal);
            var verbatim = "clean";
            var stack = new Stack<(List<string> refs, int i)>();
            stack.Push((RefsOf(formsRaw), 0));
            while (stack.Count > 0 && verbatim == "clean")
            {
                var (refs, i) = stack.Pop();
                if (i >= refs.Count) continue;
                stack.Push((refs, i + 1));
                var name = refs[i];
                // A #def rolls it and a host value replaces it — the macro text does not arrive verbatim.
                if (defs.ContainsKey(name) || hostNames.Contains(name)) continue;
                if (!macros.TryGetValue(name, out var macro) || !seenMacro.Add(name)) continue;
                // A conditional is where the engines themselves disagree: decline to judge.
                if (ConditionalOpenRe.IsMatch(macro)) verbatim = "opaque";
                else if (AnyBracketRe.IsMatch(macro)) verbatim = "brackets";
                else stack.Push((RefsOf(macro), 0));
            }
            if (verbatim == "brackets") return (0, true, true);
            if (verbatim == "opaque") return (0, true, false);

            var text = formsRaw;
            var budget = formsRaw.Length + FormExpansionMaxGrowth;
            for (var pass = 0; pass < FormExpansionPasses; pass++)
            {
                var sawReference = false;
                var bailed = false;

                // EVERY reference per pass, as the renderer's expansion does, with the budget
                // enforced DURING the pass.
                var parts = new StringBuilder();
                var total = 0;
                var cursor = 0;
                for (var m = VariableRe.Match(text); m.Success; m = m.NextMatch())
                {
                    sawReference = true;
                    var name = m.Groups[1].Value.ToLowerInvariant();
                    string? value = null;
                    if (!hostNames.Contains(name) && !defs.TryGetValue(name, out value)) macros.TryGetValue(name, out value);
                    if (value is null || AnyBracketRe.IsMatch(value))
                    {
                        bailed = true;
                        break;
                    }
                    parts.Append(text, cursor, m.Index - cursor).Append(value);
                    total += m.Index - cursor + value.Length;
                    cursor = m.Index + m.Length;
                    if (total > budget) return (0, true, false);
                }

                if (bailed) return (0, true, false);
                parts.Append(text, cursor, text.Length - cursor);
                total += text.Length - cursor;
                if (total > budget) return (0, true, false);
                text = parts.ToString();

                if (!sawReference)
                    return (text.Split('|').Length, false, false);
            }
            // A cycle, or a chain deeper than this bothers to follow.
            return (0, true, false);
        }

        private static List<string> RefsOf(string text)
        {
            var refs = new List<string>();
            foreach (Match m in VariableRe.Matches(text)) refs.Add(m.Groups[1].Value.ToLowerInvariant());
            return refs;
        }

        /// <summary><c>#set</c> names whose value is still spintax at plural time, closed over <c>#set</c> → <c>#set</c> references.</summary>
        private static HashSet<string> MacroTaintedNames(IReadOnlyDictionary<string, string> macros)
        {
            var tainted = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Stack<string>();
            foreach (var kv in macros)
            {
                if (UnresolvedAtPluralTimeRe.IsMatch(kv.Value))
                {
                    tainted.Add(kv.Key);
                    queue.Push(kv.Key);
                }
            }

            var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kv in macros)
            {
                foreach (var r in RefsOf(kv.Value))
                {
                    if (!dependents.TryGetValue(r, out var list)) dependents[r] = list = new List<string>();
                    list.Add(kv.Key);
                }
            }
            while (queue.Count > 0)
            {
                var source = queue.Pop();
                if (!dependents.TryGetValue(source, out var names)) continue;
                foreach (var name in names)
                    if (tainted.Add(name)) queue.Push(name);
            }
            return tainted;
        }

        /// <summary>Self-reference and circular definitions (errors); undefined <c>%var%</c> / conditional refs (warnings).</summary>
        private static void CheckVariableReferences(string text, LineIndex idx, IReadOnlyList<string>? known, List<Diagnostic> output)
        {
            var knownSet = LowerSet(known);
            var defs = new Dictionary<string, string>(StringComparer.Ordinal);
            var defOrder = new List<string>();
            var defPos = new Dictionary<string, Pos>(StringComparer.Ordinal);
            foreach (Match m in DefinitionLineRe.Matches(text))
            {
                var name = m.Groups[1].Value.ToLowerInvariant();
                if (!defs.ContainsKey(name)) defOrder.Add(name);
                defs[name] = m.Groups[2].Value;
                var nameOffset = m.Index + m.Value.IndexOf('%');
                defPos[name] = idx.Span(nameOffset, name.Length + 2);
            }

            var somewhere = new Pos(1, 1);
            foreach (var name in defOrder)
            {
                if (defs[name].ToLowerInvariant().Contains("%" + name + "%"))
                    output.Add(Err("variable.self-reference", $"Variable '{name}' references itself.", PosOf(defPos, name, somewhere)));
            }

            var refsOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var name in defOrder) refsOf[name] = RefsOf(defs[name]);

            // ONE diagnostic per name that takes part in, or leads to, a cycle (spintax-js#59).
            var (reaches, via) = NamesThatReachACycle(defOrder, defs, refsOf);
            foreach (var name in defOrder)
            {
                if (!reaches.Contains(name)) continue;
                output.Add(Err("variable.circular-reference", $"Circular variable reference: {CyclePath(name, via)}.",
                    PosOf(defPos, name, somewhere), Data(("name", name))));
            }

            // Blank definition lines to same-length whitespace so ref offsets still map to `text`.
            var body = DefinitionLineRe.Replace(text, m => NonNewlineRe.Replace(m.Value, " "));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void UndefinedAt(string name, int offset, int length)
            {
                var key = name.ToLowerInvariant();
                if (defs.ContainsKey(key) || knownSet.Contains(key) || !seen.Add(key)) return;
                output.Add(Warn("variable.undefined", $"Variable '{name}' is not defined — may be a runtime variable.",
                    idx.Span(offset, length), Data(("name", name))));
            }
            foreach (Match m in VariableRe.Matches(body)) UndefinedAt(m.Groups[1].Value, m.Index, m.Length);
            foreach (Match m in ConditionalRefRe.Matches(body)) UndefinedAt(m.Groups[1].Value, m.Groups[1].Index, m.Groups[1].Length);
        }

        private static Pos PosOf(Dictionary<string, Pos> defPos, string name, Pos fallback) =>
            defPos.TryGetValue(name, out var p) ? p : fallback;

        private static HashSet<string> LowerSet(IReadOnlyList<string>? names)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (names != null)
                foreach (var n in names) set.Add(n.ToLowerInvariant());
            return set;
        }

        /// <summary>
        /// Names from which a cycle of length ≥ 2 is reachable, over name → defined refs with
        /// self-edges excluded. One iterative colour walk, computed once; a witness edge per name
        /// reproduces the route the old per-path walk printed.
        /// </summary>
        private static (HashSet<string> reaches, Dictionary<string, string> via) NamesThatReachACycle(
            List<string> order, Dictionary<string, string> defs, Dictionary<string, List<string>> refsOf)
        {
            const int Grey = 1, Black = 2;
            var color = new Dictionary<string, int>(StringComparer.Ordinal);
            var reaches = new HashSet<string>(StringComparer.Ordinal);
            var via = new Dictionary<string, string>(StringComparer.Ordinal);
            void Mark(string name, string witness)
            {
                if (!reaches.Add(name)) return; // first discovery wins, so the route is deterministic
                via[name] = witness;
            }

            foreach (var root in order)
            {
                if (color.ContainsKey(root)) continue;
                var stack = new Stack<(string name, List<string> refs, int i)>();
                stack.Push((root, refsOf[root], 0));
                color[root] = Grey;
                while (stack.Count > 0)
                {
                    var (name, refs, i) = stack.Pop();
                    if (i < refs.Count)
                    {
                        stack.Push((name, refs, i + 1));
                        var r = refs[i];
                        if (r == name || !defs.ContainsKey(r)) continue;
                        if (color.TryGetValue(r, out var c))
                        {
                            if (c == Grey) Mark(name, r); // back edge — name sits on a cycle
                            else if (reaches.Contains(r)) Mark(name, r);
                        }
                        else
                        {
                            stack.Push((r, refsOf[r], 0));
                            color[r] = Grey;
                        }
                    }
                    else
                    {
                        color[name] = Black;
                        if (stack.Count > 0 && reaches.Contains(name)) Mark(stack.Peek().name, name);
                    }
                }
            }
            return (reaches, via);
        }

        /// <summary>The route from <c>name</c> into its cycle, capped past a handful of names.</summary>
        private static string CyclePath(string name, Dictionary<string, string> via)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { name };
            var shown = new List<string> { name };
            var current = name;

            while (true)
            {
                if (!via.TryGetValue(current, out var next)) break;
                if (seen.Contains(next))
                {
                    shown.Add(next); // the repeat closes the route
                    return string.Join(" → ", shown);
                }
                if (shown.Count >= CyclePathLimit)
                {
                    var more = 0;
                    var walk = next;
                    while (!seen.Contains(walk))
                    {
                        seen.Add(walk);
                        more++;
                        if (!via.TryGetValue(walk, out var step)) break;
                        walk = step;
                    }
                    return string.Join(" → ", shown) + $" → … ({more} more)";
                }
                seen.Add(next);
                shown.Add(next);
                current = next;
            }

            return string.Join(" → ", shown);
        }

        /// <summary>Unknown <c>#include</c> targets — only when a slug list is supplied.</summary>
        private static void CheckIncludeTargets(string text, LineIndex idx, IReadOnlyList<string> known, List<Diagnostic> output)
        {
            var set = new HashSet<string>(known, StringComparer.Ordinal);
            foreach (Match m in IncludeRe.Matches(text))
            {
                var reference = m.Groups[1].Value;
                if (set.Contains(reference)) continue;
                var refOffset = m.Index + m.Value.IndexOf('"') + 1; // inside the quotes
                output.Add(Err("include.unknown-target", $"#include target '{reference}' does not match any known template.",
                    idx.Span(refOffset, reference.Length), Data(("target", reference))));
            }
        }
    }
}
