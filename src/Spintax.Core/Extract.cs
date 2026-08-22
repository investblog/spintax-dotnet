using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Spintax.Core
{
    /// <summary>
    /// Port of <c>internal/extract.ts</c>: variable / <c>#set</c> / <c>#def</c> / <c>#include</c>
    /// enumeration. A raw-text scan, like the validator, so it is complete — it catches a
    /// <c>%var%</c> inside a plural count slot or a permutation body, which the tree keeps raw.
    /// Names are lower-cased (variable identity is case-insensitive); include slugs are left as
    /// authored. De-duplicated, insertion order.
    /// </summary>
    internal static class Extractor
    {
        private const string Word = "[A-Za-z0-9_]";

        // Directives use [ \t] (single-line), matching the parser's DirectiveRe — not \s, which
        // would let a malformed multi-line directive be read as a definition.
        private static readonly Regex SetDefRe = new Regex(JsText.LineStart + @"[ \t]*#set[ \t]+%(" + Word + @"+)%[ \t]*=");
        private static readonly Regex DefDefRe = new Regex(JsText.LineStart + @"[ \t]*#def[ \t]+%(" + Word + @"+)%[ \t]*=");
        private static readonly Regex DefinitionLhsRe = new Regex(JsText.LineStart + @"[ \t]*#(?:set|def)[ \t]+%" + Word + @"+%[ \t]*=");
        // The gap class is spelled out because no dialect's `\s` is this set. Corpus-pinned.
        private static readonly Regex IncludeRe = new Regex(JsText.LineStart + @"[ \t]*#include[ \t\n\r\f\x0B]+""([^""]+)""[ \t\n\r\f\x0B]*" + JsText.LineEnd);
        private static readonly Regex VariableRe = new Regex("%(" + Word + "+)%");
        private static readonly Regex ConditionalRefRe = new Regex(@"\{\?!?([A-Za-z_]" + Word + @"*)\?");

        public static ExtractResult ExtractFromSource(string src)
        {
            var text = Parser.StripComments(src);

            var sets = Collect(text, SetDefRe, fold: true);
            var defs = Collect(text, DefDefRe, fold: true);
            var includes = Collect(text, IncludeRe, fold: false);

            // Drop each `#set`/`#def … =` LHS (keep the value) so a definition target is not a ref.
            var body = DefinitionLhsRe.Replace(text, "");
            var refs = new OrderedSet();
            AddAll(refs, body, VariableRe, fold: true);
            AddAll(refs, body, ConditionalRefRe, fold: true);

            return new ExtractResult(refs.Items, sets, defs, includes);
        }

        private static IReadOnlyList<string> Collect(string text, Regex re, bool fold)
        {
            var seen = new OrderedSet();
            AddAll(seen, text, re, fold);
            return seen.Items;
        }

        private static void AddAll(OrderedSet target, string text, Regex re, bool fold)
        {
            foreach (Match m in re.Matches(text))
            {
                var value = m.Groups[1].Value;
                if (value.Length > 0) target.Add(fold ? value.ToLowerInvariant() : value);
            }
        }

        /// <summary>Insertion-ordered, de-duplicated — what a JS <c>Set</c> spreads to.</summary>
        private sealed class OrderedSet
        {
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<string> _items = new List<string>();

            public void Add(string s)
            {
                if (_seen.Add(s)) _items.Add(s);
            }

            public IReadOnlyList<string> Items => _items;
        }
    }
}
