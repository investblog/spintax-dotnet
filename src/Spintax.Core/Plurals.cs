using System;
using System.Collections.Generic;

namespace Spintax.Core
{
    /// <summary>A raw <c>{plural …}</c> block found by <see cref="Plurals.FindPluralBlocks"/>.</summary>
    internal sealed class PluralBlock
    {
        public PluralBlock(int start, int end, string countSlot, string formsRaw)
        {
            Start = start;
            End = end;
            CountSlot = countSlot;
            FormsRaw = formsRaw;
        }

        public int Start { get; }

        /// <summary>Exclusive — one past the block's closing <c>}</c>.</summary>
        public int End { get; }

        public string CountSlot { get; }

        public string FormsRaw { get; }
    }

    /// <summary>Port of <c>internal/plurals.ts</c> — locale plural rules (spec §3.1).</summary>
    internal static class Plurals
    {
        private const string PluralPrefix = "{plural ";

        /// <summary>
        /// Brace-aware scan for <c>{plural …}</c> blocks over the whole text, permutation bodies
        /// included. A block without a <c>:</c> is not a plural (left to the enumeration path).
        /// </summary>
        public static List<PluralBlock> FindPluralBlocks(string text)
        {
            var blocks = new List<PluralBlock>();
            var i = 0;
            while (i < text.Length)
            {
                var start = text.IndexOf(PluralPrefix, i, StringComparison.Ordinal);
                if (start < 0) break;

                var depth = 1;
                var j = start + PluralPrefix.Length;
                while (j < text.Length)
                {
                    var ch = text[j];
                    if (ch == '{') depth++;
                    else if (ch == '}')
                    {
                        depth--;
                        if (depth == 0) break;
                    }
                    j++;
                }
                if (depth != 0)
                {
                    i = start + PluralPrefix.Length; // unmatched opening — skip past the prefix
                    continue;
                }

                var inner = text.Substring(start + PluralPrefix.Length, j - (start + PluralPrefix.Length));
                var colon = inner.IndexOf(':');
                if (colon < 0)
                {
                    i = j + 1; // no colon ⇒ not a plural
                    continue;
                }
                blocks.Add(new PluralBlock(start, j + 1, inner.Substring(0, colon), inner.Substring(colon + 1)));
                i = j + 1;
            }
            return blocks;
        }

        /// <summary>
        /// A locale's base language tag: <c>pt-BR</c>→<c>pt</c>, <c>uk_UA</c>→<c>uk</c>,
        /// <c>RU</c>→<c>ru</c>. Absent or empty gives <c>""</c>. A 3-letter tag is returned
        /// whole — this knows nothing of ISO-639-3.
        /// </summary>
        public static string NormalizeBaseLang(string? locale)
        {
            if (string.IsNullOrEmpty(locale)) return "";
            var lower = locale!.ToLowerInvariant();
            var cut = lower.IndexOfAny(new[] { '-', '_' });
            return cut < 0 ? lower : lower.Substring(0, cut);
        }

        /// <summary>
        /// Forms a base language takes: 6 for Arabic (CLDR zero/one/two/few/many/other), 3 for
        /// the one/few/other family (East Slavic ru/uk/be + BCS sr/hr/bs), else 2. Takes a BASE
        /// language, not a raw locale. Arabic is strict like the others (spintax-js#88): a
        /// two-form <c>ar</c> block is <c>plural.arity</c>.
        /// </summary>
        public static int PluralArity(string baseLang)
        {
            switch (baseLang)
            {
                case "ar":
                    return 6;
                case "ru":
                case "uk":
                case "be":
                case "sr":
                case "hr":
                case "bs":
                    return 3;
                default:
                    return 2;
            }
        }

        /// <summary>
        /// How many forms <c>render</c> resolves against with no locale — derived from the
        /// table, not written as <c>2</c>, so the validator's <c>plural.locale-missing</c> cannot
        /// drift from what render does (spintax-js#65).
        /// </summary>
        public static readonly int DefaultPluralArity = PluralArity("");

        /// <summary>
        /// The form for a count by the locale's grammar. Slavic: one (1, 21, 31… not 11), few
        /// (2–4, 22–24… not 12–14), many (the rest, 0 included). Arabic, CLDR order: zero (0),
        /// one (1), two (2), few (n % 100 in 3..10), many (n % 100 in 11..99), other (the rest:
        /// 100–102, 200–202, …). EN-style: one (n = 1), many.
        /// Negative counts use the absolute value. A missing form is <c>""</c>.
        /// </summary>
        /// <remarks>
        /// <c>double</c>, like the reference's JS number: a count past 2^53 loses precision the
        /// same way, and an infinite one (a digit run past the double range) gives NaN remainders
        /// whose comparisons are all false — the last branch, exactly as in JS: "many" for the
        /// Slavic and English rules, "other" (<c>forms[5]</c>) for Arabic. Never throws.
        /// </remarks>
        public static string PluralFor(string baseLang, double n, IReadOnlyList<string> forms)
        {
            var abs = Math.Abs(n);
            var mod10 = abs % 10;
            var mod100 = abs % 100;

            switch (baseLang)
            {
                case "ar":
                    if (abs == 0) return At(forms, 0);
                    if (abs == 1) return At(forms, 1);
                    if (abs == 2) return At(forms, 2);
                    if (mod100 >= 3 && mod100 <= 10) return At(forms, 3);
                    if (mod100 >= 11) return At(forms, 4);
                    return At(forms, 5);
                case "ru":
                case "uk":
                case "be":
                case "sr":
                case "hr":
                case "bs":
                    if (mod10 == 1 && mod100 != 11) return At(forms, 0);
                    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return At(forms, 1);
                    return At(forms, 2);
                default:
                    return abs == 1 ? At(forms, 0) : At(forms, 1);
            }
        }

        private static string At(IReadOnlyList<string> forms, int i) => i < forms.Count ? forms[i] : "";
    }
}
