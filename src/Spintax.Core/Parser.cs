using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Spintax.Core
{
    /// <summary>One directive occurrence, in source order, with the line it was written on.</summary>
    internal sealed class DirectiveOccurrence
    {
        public DirectiveOccurrence(string kind, string name, string value, int line)
        {
            Kind = kind;
            Name = name;
            Value = value;
            Line = line;
        }

        /// <summary><c>"set"</c> or <c>"def"</c>.</summary>
        public string Kind { get; }

        /// <summary>Lower-cased.</summary>
        public string Name { get; }

        public string Value { get; }

        /// <summary>1-based.</summary>
        public int Line { get; }
    }

    /// <summary>Result of the global directive pass: the body with directive lines removed, and the maps.</summary>
    internal sealed class ExtractedDirectives
    {
        public ExtractedDirectives(string body, Dictionary<string, string> setDefs, Dictionary<string, string> defDefs,
            List<DirectiveOccurrence> occurrences)
        {
            Body = body;
            SetDefs = setDefs;
            DefDefs = defDefs;
            Occurrences = occurrences;
        }

        public string Body { get; }

        public Dictionary<string, string> SetDefs { get; }

        public Dictionary<string, string> DefDefs { get; }

        /// <summary>Every directive line including the duplicates the maps flatten away.</summary>
        public List<DirectiveOccurrence> Occurrences { get; }
    }

    /// <summary>A recognised <c>{?…}</c> as OFFSETS into the string it was found in.</summary>
    internal sealed class ConditionalHead
    {
        public ConditionalHead(string name, bool inverted, int bodyStart, int sepIndex)
        {
            Name = name;
            Inverted = inverted;
            BodyStart = bodyStart;
            SepIndex = sepIndex;
        }

        public string Name { get; }

        public bool Inverted { get; }

        /// <summary>Offset of the body (past <c>?name?</c>).</summary>
        public int BodyStart { get; }

        /// <summary>Offset of the top-level <c>|</c>, or -1 when the branch stands alone.</summary>
        public int SepIndex { get; }
    }

    /// <summary>A recognised <c>{?…}</c> with its branches materialised — what the parser needs.</summary>
    internal sealed class ConditionalParts
    {
        public ConditionalParts(string name, bool inverted, string thenRaw, string elseRaw)
        {
            Name = name;
            Inverted = inverted;
            ThenRaw = thenRaw;
            ElseRaw = elseRaw;
        }

        public string Name { get; }

        public bool Inverted { get; }

        public string ThenRaw { get; }

        public string ElseRaw { get; }
    }

    /// <summary>
    /// Port of <c>internal/parser.ts</c>: template string → <see cref="ParsedAst"/>. Lenient by
    /// contract (spec §9.2) — never throws on malformed markup; a bad conditional or plural falls
    /// back to an enumeration, an unmatched bracket is literal text. Structural diagnostics are
    /// the validator's job.
    /// </summary>
    /// <remarks>
    /// Dialect notes (<c>docs/TODO.md</c>, "Правила порта"): the reference's <c>\w</c> is ASCII
    /// and is spelled <c>[A-Za-z0-9_]</c>; its sticky <c>/y</c> regex is <c>\G</c> with a start
    /// index; its multiline <c>^</c>/<c>$</c> and <c>.</c> see all four JS line terminators, so
    /// they are <see cref="JsText.LineStart"/> / <see cref="JsText.LineEnd"/> / <see cref="JsText.Dot"/>,
    /// not <c>RegexOptions.Multiline</c>; its <c>\s</c> is <see cref="JsText.S"/>.
    /// </remarks>
    internal static class Parser
    {
        private const string Word = "[A-Za-z0-9_]";

        private static readonly Regex VariableRe = new Regex(@"\G%(" + Word + "+)%");

        /// <summary>
        /// The one grammar for <c>#set</c> and <c>#def</c>. Whitespace is <c>[ \t]</c> (not
        /// <c>\s</c>) so a directive is a single line; the value group is lazy so an empty value
        /// is legal; <c>\r?</c> before the line end so a CRLF line strips cleanly.
        /// </summary>
        internal static readonly Regex DirectiveRe = new Regex(
            JsText.LineStart + @"[ \t]*#(set|def)[ \t]+%(" + Word + @"+)%[ \t]*=[ \t]*(" + JsText.Dot + @"*?)[ \t]*\r?" + JsText.LineEnd);

        private static readonly Regex ConditionalNameRe = new Regex(@"\G[A-Za-z_]" + Word + "*");
        private static readonly Regex CommentRe = new Regex(@"/#[\s\S]*?#/");
        private static readonly Regex BlankLinesRe = new Regex("\n{3,}");

        private const string PluralPrefix = "plural ";

        /// <summary>
        /// Parse a full template: sanitise (stray sentinels out), strip comments, extract
        /// directives, build the tree. <see cref="ParsedAst.Source"/> keeps the ORIGINAL text —
        /// the validator places diagnostics on the bytes the author wrote.
        /// </summary>
        public static ParsedAst ParseTemplate(string src)
        {
            var d = ExtractDirectives(StripComments(Shield.StripSentinels(src)));
            return new ParsedAst(src, d.SetDefs, d.DefDefs, ParseSequence(d.Body));
        }

        /// <summary>
        /// Global directive extraction: every line-anchored <c>#set</c>/<c>#def</c>, regardless
        /// of brace nesting, name lower-cased; the lines are removed and <c>\n{3,}</c> collapses
        /// to <c>\n\n</c>. Line numbers resume from the previous match (a fresh count per
        /// occurrence made a directive-heavy document quadratic).
        /// </summary>
        public static ExtractedDirectives ExtractDirectives(string text)
        {
            var setDefs = new Dictionary<string, string>(StringComparer.Ordinal);
            var defDefs = new Dictionary<string, string>(StringComparer.Ordinal);
            var occurrences = new List<DirectiveOccurrence>();

            var cursorOffset = 0;
            var cursorLine = 1;

            var stripped = DirectiveRe.Replace(text, m =>
            {
                for (var i = cursorOffset; i < m.Index; i++)
                    if (text[i] == '\n') cursorLine++;
                cursorOffset = m.Index;

                var kind = m.Groups[1].Value;
                var name = m.Groups[2].Value.ToLowerInvariant();
                var value = m.Groups[3].Value;
                occurrences.Add(new DirectiveOccurrence(kind, name, value, cursorLine));
                if (kind == "def") defDefs[name] = value;
                else setDefs[name] = value;
                return "";
            });

            return new ExtractedDirectives(BlankLinesRe.Replace(stripped, "\n\n"), setDefs, defDefs, occurrences);
        }

        /// <summary>Remove <c>/# … #/</c> block comments (non-greedy, spans newlines).</summary>
        public static string StripComments(string text) => CommentRe.Replace(text, "");

        // ── the tree ─────────────────────────────────────────────────────────────────────

        /// <summary>A construct whose children still need parsing: their raw texts, and how to assemble the node.</summary>
        private sealed class ChildPlan
        {
            public ChildPlan(IReadOnlyList<string> texts, Func<List<IReadOnlyList<Node>>, Node> build)
            {
                Texts = texts;
                Build = build;
            }

            public IReadOnlyList<string> Texts { get; }

            public Func<List<IReadOnlyList<Node>>, Node> Build { get; }
        }

        /// <summary>Either a finished node or a plan for one.</summary>
        private sealed class Planned
        {
            public Planned(Node node) { Node = node; }

            public Planned(ChildPlan plan) { Plan = plan; }

            public Node? Node { get; }

            public ChildPlan? Plan { get; }
        }

        private sealed class Frame
        {
            public Frame(string text) { Text = text; }

            public string Text { get; }

            public int I;
            public readonly StringBuilder Literal = new StringBuilder();
            public readonly List<Node> Nodes = new List<Node>();
            public ChildPlan? Plan;
            public List<IReadOnlyList<Node>> Parts = new List<IReadOnlyList<Node>>();

            public void FlushLiteral()
            {
                if (Literal.Length == 0) return;
                Nodes.Add(new LiteralNode(Literal.ToString()));
                Literal.Clear();
            }
        }

        /// <summary>
        /// Parse a run of text into a node sequence — construct parsing only, no sanitising, no
        /// comment strip, no directive extraction; the renderer uses this to re-process variable
        /// values, where sentinels a host neutralised are legitimate. Iterative (#68): one frame
        /// per nesting level overflowed at ~2000 levels, and the engine never throws on content.
        /// </summary>
        public static IReadOnlyList<Node> ParseSequence(string text)
        {
            var stack = new Stack<Frame>();
            stack.Push(new Frame(text));

            while (stack.Count > 0)
            {
                var f = stack.Peek();

                // A construct is mid-flight: descend into its next child, or assemble it.
                if (f.Plan != null)
                {
                    if (f.Parts.Count < f.Plan.Texts.Count)
                    {
                        stack.Push(new Frame(f.Plan.Texts[f.Parts.Count]));
                        continue;
                    }
                    f.Nodes.Add(f.Plan.Build(f.Parts));
                    f.Plan = null;
                    f.Parts = new List<IReadOnlyList<Node>>();
                    continue;
                }

                Planned? planned = null;
                while (f.I < f.Text.Length && planned is null)
                {
                    var ch = f.Text[f.I];

                    if (ch == '{' || ch == '[')
                    {
                        var close = ch == '{' ? '}' : ']';
                        var end = FindMatchingClose(f.Text, f.I, ch, close);
                        if (end < 0)
                        {
                            f.Literal.Append(ch);
                            f.I++;
                            continue;
                        }
                        var inner = f.Text.Substring(f.I + 1, end - f.I - 1);
                        f.FlushLiteral();
                        planned = ch == '{' ? PlanBraceConstruct(inner) : PlanPermutation(inner);
                        f.I = end + 1;
                        continue;
                    }

                    if (ch == '%')
                    {
                        var m = VariableRe.Match(f.Text, f.I);
                        if (m.Success)
                        {
                            var name = m.Groups[1].Value;
                            f.FlushLiteral();
                            f.Nodes.Add(new VariableNode(name));
                            f.I += name.Length + 2; // "%" + name + "%"
                            continue;
                        }
                    }

                    f.Literal.Append(ch);
                    f.I++;
                }

                if (planned != null)
                {
                    if (planned.Node != null) f.Nodes.Add(planned.Node);
                    else f.Plan = planned.Plan;
                    continue;
                }

                // Frame exhausted: finish it and hand its nodes to the parent's pending construct.
                f.FlushLiteral();
                stack.Pop();
                if (stack.Count > 0) stack.Peek().Parts.Add(f.Nodes);
                else return f.Nodes;
            }

            return Array.Empty<Node>();
        }

        /// <summary>
        /// What a <c>{…}</c> is: a conditional (<c>?…</c>), a plural (<c>plural …:</c>), or — the
        /// default and the fallback for a malformed conditional — an enumeration.
        /// </summary>
        private static Planned PlanBraceConstruct(string content)
        {
            if (content.Length > 0 && content[0] == '?')
            {
                var parts = SplitConditional(content);
                if (parts != null)
                {
                    return new Planned(new ChildPlan(
                        new[] { parts.ThenRaw, parts.ElseRaw },
                        children => new ConditionalNode(parts.Name, parts.Inverted, children[0], children[1])));
                }
                // Malformed conditional ⇒ enumeration (plugin parity).
            }
            else if (content.StartsWith(PluralPrefix, StringComparison.Ordinal)
                     && content.IndexOf(':', PluralPrefix.Length) >= 0)
            {
                return new Planned(ParsePlural(content.Substring(PluralPrefix.Length)));
            }
            return new Planned(new ChildPlan(
                SplitTopLevel(content),
                children => new EnumerationNode(children)));
        }

        /// <summary><c>[&lt;config&gt;a|b|c]</c> — config and per-element separators resolve here; the elements are parsed by the loop.</summary>
        private static Planned PlanPermutation(string rawInner)
        {
            var (config, content) = ExtractPermutationConfig(rawInner);
            var (texts, separators) = PermutationElements(SplitTopLevel(content));
            return new Planned(new ChildPlan(texts, children =>
            {
                var options = new List<PermOption>(children.Count);
                for (var i = 0; i < children.Count; i++)
                    options.Add(new PermOption(children[i], i < separators.Count ? separators[i] : null));
                return new PermutationNode(config, options);
            }));
        }

        /// <summary>
        /// Recognise <c>?VAR?then|else</c> / <c>?!VAR?then</c> in <c>text[contentStart, contentEnd)</c>
        /// — the ONE place the conditional grammar lives; the renderer's count-slot pass reads
        /// it too. Offsets only.
        /// </summary>
        public static ConditionalHead? RecognizeConditional(string text, int contentStart, int contentEnd)
        {
            var p = contentStart + 1; // past the leading '?'
            var inverted = false;
            if (p < text.Length && text[p] == '!')
            {
                inverted = true;
                p++;
            }

            var m = ConditionalNameRe.Match(text, p);
            if (!m.Success || p + m.Length > contentEnd) return null;
            var name = m.Value;
            p += name.Length;

            if (p >= text.Length || text[p] != '?') return null; // required '?' after the name
            p++;

            return new ConditionalHead(name, inverted, p, FirstTopLevelPipe(text, p, contentEnd));
        }

        public static ConditionalParts? SplitConditional(string content)
        {
            var head = RecognizeConditional(content, 0, content.Length);
            if (head is null) return null;

            var body = content.Substring(head.BodyStart);
            var sep = head.SepIndex < 0 ? -1 : head.SepIndex - head.BodyStart;

            return new ConditionalParts(head.Name, head.Inverted,
                sep < 0 ? body : body.Substring(0, sep),
                sep < 0 ? "" : body.Substring(sep + 1));
        }

        /// <summary><c>&lt;count&gt;: forms</c> (after the <c>plural </c> prefix); both slots stay raw for the renderer.</summary>
        private static Node ParsePlural(string afterPrefix)
        {
            var colon = afterPrefix.IndexOf(':');
            return new PluralNode(afterPrefix.Substring(0, colon), afterPrefix.Substring(colon + 1));
        }

        // ── permutation config + per-element separators ──────────────────────────────────

        private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private const string B = @"(?:(?<![A-Za-z0-9_])(?=[A-Za-z0-9_])|(?<=[A-Za-z0-9_])(?![A-Za-z0-9_]))";

        private static readonly Regex ConfigKeyRe = new Regex(B + "(?:minsize|maxsize|sep|lastsep)" + JsText.S + "*=", Ci);
        private static readonly Regex MinSizeRe = new Regex("minsize" + JsText.S + "*=" + JsText.S + "*([0-9]+)", Ci);
        private static readonly Regex MaxSizeRe = new Regex("maxsize" + JsText.S + "*=" + JsText.S + "*([0-9]+)", Ci);
        private static readonly Regex SepRe = new Regex("(?<!last)sep" + JsText.S + "*=" + JsText.S + "*\"([^\"]*)\"", Ci);
        private static readonly Regex LastSepRe = new Regex("lastsep" + JsText.S + "*=" + JsText.S + "*\"([^\"]*)\"", Ci);
        private static readonly Regex HtmlTagRe = new Regex("^([a-zA-Z][a-zA-Z0-9-]*)(?:" + JsText.S + @"+[^>]*)?\/?\z");
        private static readonly Regex PerElemHtmlRe = new Regex("^[a-zA-Z][a-zA-Z0-9]*" + JsText.S);

        private static PermConfig DefaultPermConfig() => new PermConfig(null, null, " ", null);

        /// <summary>Split a leading <c>&lt;config&gt;</c> off the body — BEFORE the top-level split, so a <c>|</c> inside <c>sep="|"</c> is not a split.</summary>
        private static (PermConfig config, string content) ExtractPermutationConfig(string content)
        {
            var trimmed = PhpLtrim(content);
            if (trimmed.Length == 0 || trimmed[0] != '<') return (DefaultPermConfig(), content);

            var end = FindConfigEnd(trimmed);
            if (end < 0) return (DefaultPermConfig(), content);

            var configStr = trimmed.Substring(1, end - 1);
            var remaining = trimmed.Substring(end + 1);
            // A leading `<li>…</li>`-style tag is HTML, not config.
            if (LooksLikeHtmlStartTag(configStr, remaining)) return (DefaultPermConfig(), content);
            return (ParseConfigString(configStr), remaining);
        }

        /// <summary>Index of the closing <c>&gt;</c> of a <c>&lt;…&gt;</c> config, respecting quoted strings; -1 if none.</summary>
        private static int FindConfigEnd(string text)
        {
            var inQuote = false;
            for (var i = 1; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '"') inQuote = !inQuote;
                if (ch == '>' && !inQuote) return i;
            }
            return -1;
        }

        private static PermConfig ParseConfigString(string str)
        {
            if (!ConfigKeyRe.IsMatch(str))
            {
                // Single-separator form: the whole string is sep (and lastsep).
                return new PermConfig(null, null, str, str);
            }
            return new PermConfig(
                IntGroup(MinSizeRe.Match(str)),
                IntGroup(MaxSizeRe.Match(str)),
                StrGroup(SepRe.Match(str)) ?? " ",
                StrGroup(LastSepRe.Match(str)));
        }

        private static bool LooksLikeHtmlStartTag(string tagText, string remaining)
        {
            var trimmed = PhpTrim(tagText);
            if (trimmed.Length == 0) return false;
            var m = HtmlTagRe.Match(trimmed);
            if (!m.Success) return false;
            if (trimmed.EndsWith("/", StringComparison.Ordinal)) return true; // self-closing
            var tagName = m.Groups[1].Value.ToLowerInvariant();
            return Regex.IsMatch(remaining, "</" + Regex.Escape(tagName) + JsText.S + "*>", Ci);
        }

        /// <summary>
        /// Raw split parts → elements: a trailing <c>&lt;sep&gt;</c> on part[i] becomes the
        /// per-element separator of the element from part[i+1]; element text is PHP-trimmed;
        /// empty elements are dropped.
        /// </summary>
        private static (List<string> texts, List<string?> separators) PermutationElements(IReadOnlyList<string> rawParts)
        {
            var texts = new List<string>();
            var separators = new List<string?>();
            string? pendingSep = null;

            for (var i = 0; i < rawParts.Count; i++)
            {
                var text = rawParts[i];
                string? trailingSep = null;
                if (i < rawParts.Count - 1)
                {
                    var extracted = ExtractTrailingSep(text);
                    if (extracted != null)
                    {
                        text = extracted.Value.text;
                        trailingSep = extracted.Value.sep;
                    }
                }
                var trimmed = PhpTrim(text);
                if (trimmed.Length > 0)
                {
                    texts.Add(trimmed);
                    separators.Add(pendingSep);
                }
                pendingSep = trailingSep;
            }

            return (texts, separators);
        }

        /// <summary>A trailing <c>&lt; sep &gt;</c> on a part that is not an HTML tag.</summary>
        private static (string text, string sep)? ExtractTrailingSep(string part)
        {
            var trimmed = PhpRtrim(part);
            var len = trimmed.Length;
            if (len == 0 || trimmed[len - 1] != '>') return null;

            var openPos = -1;
            for (var i = len - 2; i >= 0; i--)
            {
                var ch = trimmed[i];
                if (ch == '<')
                {
                    openPos = i;
                    break;
                }
                if (ch == '>') return null; // nested/complex, bail
            }
            if (openPos < 0) return null;

            var inner = trimmed.Substring(openPos + 1, len - 1 - (openPos + 1));
            var innerTrimmed = PhpTrim(inner);
            // HTML tag → not a separator: closing </x>, self-closing <x/>, or tag-with-attrs `<x …>`.
            if (innerTrimmed.StartsWith("/", StringComparison.Ordinal)
                || innerTrimmed.EndsWith("/", StringComparison.Ordinal)
                || PerElemHtmlRe.IsMatch(innerTrimmed))
                return null;
            return (trimmed.Substring(0, openPos), inner);
        }

        private static int? IntGroup(Match m)
        {
            if (!m.Success) return null;
            // JS parseInt never overflows — a huge count is just a huge number; int.MaxValue plays that part.
            return int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : int.MaxValue;
        }

        private static string? StrGroup(Match m) => m.Success ? m.Groups[1].Value : null;

        // ── PHP trim — [ \t\n\r\0\x0B] only, where the plugin trims ──────────────────────

        private static bool IsPhpSpace(char ch) =>
            ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r' || ch == '\0' || ch == '\v';

        public static string PhpTrim(string s) => PhpRtrim(PhpLtrim(s));

        public static string PhpLtrim(string s)
        {
            var i = 0;
            while (i < s.Length && IsPhpSpace(s[i])) i++;
            return i == 0 ? s : s.Substring(i);
        }

        public static string PhpRtrim(string s)
        {
            var end = s.Length;
            while (end > 0 && IsPhpSpace(s[end - 1])) end--;
            return end == s.Length ? s : s.Substring(0, end);
        }

        // ── bracket helpers ──────────────────────────────────────────────────────────────

        /// <summary>Index of the <c>close</c> matching the <c>open</c> at <c>openPos</c>, this bracket pair only; -1 if unmatched.</summary>
        public static int FindMatchingClose(string text, int openPos, char open, char close)
        {
            var depth = 0;
            for (var i = openPos; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == open) depth++;
                else if (ch == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Split on top-level <c>|</c>: brace and bracket depths tracked INDEPENDENTLY and
        /// decremented UNCONDITIONALLY (may go negative), split only when BOTH are exactly 0 —
        /// so <c>a]|b</c> stays one option.
        /// </summary>
        public static List<string> SplitTopLevel(string inner)
        {
            var parts = new List<string>();
            var brace = 0;
            var bracket = 0;
            var cur = new StringBuilder();
            foreach (var ch in inner)
            {
                if (ch == '{') brace++;
                else if (ch == '}') brace--;
                else if (ch == '[') bracket++;
                else if (ch == ']') bracket--;

                if (ch == '|' && brace == 0 && bracket == 0)
                {
                    parts.Add(cur.ToString());
                    cur.Clear();
                }
                else cur.Append(ch);
            }
            parts.Add(cur.ToString());
            return parts;
        }

        /// <summary>
        /// First top-level <c>|</c> in a conditional body, or -1. ONE depth counter CLAMPED at 0
        /// — the plugin's conditional split, which differs from <see cref="SplitTopLevel"/>.
        /// </summary>
        public static int FirstTopLevelPipe(string body, int from, int to)
        {
            var depth = 0;
            for (var j = from; j < to; j++)
            {
                var ch = body[j];
                if (ch == '{' || ch == '[') depth++;
                else if (ch == '}' || ch == ']')
                {
                    if (depth > 0) depth--;
                }
                else if (ch == '|' && depth == 0) return j;
            }
            return -1;
        }
    }
}
