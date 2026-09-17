using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Spintax.Core
{
    /// <summary>
    /// Port of <c>internal/postprocess.ts</c> — the cosmetic stage (spec §5), parity-gated by the
    /// corpus. Order matters: URIs / emails / domains / decimals / abbreviations are shielded
    /// to <c>\0PREFIX_n\0</c> placeholders FIRST so the spacing and capitalisation passes cannot
    /// corrupt them, then restored and trimmed.
    /// </summary>
    /// <remarks>
    /// Every regex is the reference's, rewritten for the .NET dialect where the two disagree
    /// (<c>docs/TODO.md</c>, "Правила порта"): no shorthand class is written, because PHP's
    /// <c>/u</c> makes them Unicode and .NET's are a third set again — they come from
    /// <see cref="CharClass"/>, UCP everywhere but the decimal shield, which PHP writes without
    /// <c>/u</c>; JS <c>$</c> (no <c>m</c> flag) → <c>\z</c>, because .NET's <c>$</c> also matches
    /// before a final newline; JS <c>trim()</c> and <c>toUpperCase()</c> → <see cref="JsText"/>,
    /// which stay JS semantics: the final trim of this stage is JavaScript's.
    /// Static <c>Regex</c> instances are immutable and thread-safe.
    /// </remarks>
    internal static class PostProcessor
    {
        // Single-token abbreviations (case-insensitive) that would otherwise look like a
        // sentence end. Multi-dot forms (т.д.) are handled by the MultiAbbr regex.
        private const string SingleAbbrevs =
            "соц|эл|см|ср|ст|ул|пр|пер|г|р|руб|коп|тыс|млн|млрд|трлн|доп|напр|прим|изд|обл|респ|" +
            "стр|табл|рис|мин|макс|тел|факс|" +
            "etc|vs|Mr|Mrs|Ms|Dr|Prof|Sr|Jr|Inc|Ltd|Co|Corp|No|St|Ave|Blvd";

        // Every pattern of this stage carries /u in PHP — the decimal shield alone does not — and
        // /u is PCRE2_UCP: `\s` takes NBSP and the rest of \p{Z}, `\b` and `\d` see every script.
        // So the classes are UCP, spelled out (CharClass); this block once said the opposite, and
        // Cyrillic abbreviations, IDN domains and NBSP were mangled here while PHP rendered them
        // intact (spintax-js#81).
        private const string Ws = CharClass.UcpSpaceChars;
        private const string S = CharClass.UcpSpace;

        // A leading `\b` in front of a pattern that begins with a word character, and `\b` in
        // general — both UCP, so a Cyrillic letter is a word character here as it is in PHP.
        private const string AfterNonWord = CharClass.UcpAfterNonWord;
        private const string B = CharClass.UcpWordBoundary;

        // A punycode label as the plugin reads `xn--` under `i`: either case, and the two non-ASCII
        // letters that fold into `[a-z]` — U+017F LONG S and U+212A KELVIN SIGN. Spelled out,
        // because the domain patterns below carry no `i`.
        private const string Xn = "[xX][nN]--";
        private const string Label = "(?:" + Xn + @")?[\p{L}\p{N}]+(?:-[\p{L}\p{N}]+)*";

        // A TLD is a label in ONE case: `example.com` and `ASP.NET` are domains, `compact.Game` is
        // a sentence glued to the next one, and so is `конец.Начало` (spintax-js#79). Letters
        // without case (\p{Lo}, \p{Lm} — CJK, Arabic, Thai) fit either reading, so `例子.中国` stays
        // a domain. The accepted cost, pinned by the corpus: `Yandex.Money` renders
        // `Yandex. Money`, and `info@example.Com` is no longer shielded as an email.
        //
        // PHP writes the one-case alternative under `(?-i:…)`. .NET has inline modifiers, but not
        // the case-folding to go with them: `RegexOptions.IgnoreCase` folds by an equivalence table
        // on net8 and by lower-casing the input character on net472, and the two differ on exactly
        // U+017F and U+212A. So the domain patterns carry no `i` on either host and spell out the
        // one part that is case-insensitive — the punycode form.
        private const string TldLower = @"\p{Ll}\p{Lm}\p{Lo}";
        private const string TldUpper = @"\p{Lu}\p{Lt}\p{Lm}\p{Lo}";
        private const string Tld =
            "(?:" + Xn + "[a-zA-Z0-9\\-\\u017F\\u212A]{2,59}"
            + "|[" + TldLower + "][" + TldLower + @"\p{N}-]{1,62}"
            + "|[" + TldUpper + "][" + TldUpper + @"\p{N}-]{1,62})";

        private const string DomainPart = "(?:" + Label + @"\.)+" + Tld;

        // URIs — https?/ftp (with an authority) and mailto:/tel: (without one) — in ONE pass, so
        // an overlapping pair is never split (spintax-js#53). `\0` stays out of the body class.
        private const string UriBody = @"[^\x00" + Ws + @"<>""')\]]";

        private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex UriRe = new Regex(@"(?:(?:https?|ftp):\/\/|(?:mailto|tel):)" + UriBody + "+", Ci);
        private static readonly Regex MailTelPrefixRe = new Regex(@"^(?:mailto|tel):", Ci);
        // `[a-z0-9._%+-]` as the plugin's pattern reads it under `iu`: both cases, and the two
        // non-ASCII letters that fold into the class. No `i` here either — see Tld above.
        private const string EmailLocal = "[a-zA-Z0-9._%+\\-\\u017F\\u212A]";

        private static readonly Regex EmailRe = new Regex(EmailLocal + "+@" + DomainPart + B);
        private static readonly Regex DomainRe = new Regex(AfterNonWord + DomainPart + B);
        // PHP's decimal shield is the one pattern here without /u: byte mode, so its `\b` and `\d`
        // are ASCII. Deliberately not widened with the rest.
        private static readonly Regex DecimalRe =
            new Regex(CharClass.AsciiWordBoundary + @"[0-9]+\.[0-9]+" + CharClass.AsciiWordBoundary);
        private static readonly Regex MultiAbbrRe = new Regex(AfterNonWord + @"(?:\p{L}{1,2}\." + S + "*){2,}");
        private static readonly Regex SingleAbbrRe = new Regex(@"(?<![\p{L}\p{N}])(?:" + SingleAbbrevs + @")\.(?=" + S + @"|\z|<)", Ci);
        private static readonly Regex TrailingPunctRe = new Regex(@"([.,;:!]+)\z");

        // The inverted marks that OPEN a Spanish question/exclamation — deliberately NOT widened
        // to quotes/brackets, which both open and close.
        private const string SentenceOpeners = "¿¡";
        // Everything that can sit between a sentence boundary and the first letter.
        private const string Lead = "(?:<[^>]+>|[" + SentenceOpeners + "]|" + S + ")*";

        private static readonly Regex CollapseSpacesRe = new Regex("[ \t]{2,}");
        // A match may start only where a whitespace run starts (`(?<!S)`): every start inside a run
        // reaches the same end, so the same matches — but a run NOT followed by punctuation is
        // scanned once instead of once per character, and the UCP class above gave NBSP and U+3000
        // runs the same shape (spintax-js#80).
        private static readonly Regex SpaceBeforePunctRe = new Regex("(?<!" + S + ")" + S + "+([,;:!?.])");
        // The digit is PHP's UCP `\d` — any decimal digit, not just ASCII.
        private static readonly Regex SpaceAfterCommaRe = new Regex(@"([,;:])(?!\p{Nd})(?!" + S + @"|\z|<)");
        // A run of sentence punctuation is ONE sentence end; `(?![.!?])` completes the run, and
        // `(?<![.!?])` starts the match only where the run starts, for the same reason as above.
        private static readonly Regex SpaceAfterSentenceRe =
            new Regex(@"(?<![.!?])([.!?]+)(?![.!?])(?!\p{Nd})(?!" + S + @"|\z|<)");
        // An opener binds to the word it opens. MUST run before capitalisation.
        private static readonly Regex SpaceAfterOpenerRe = new Regex("([" + SentenceOpeners + "])" + S + "+");
        private static readonly Regex CapFirstRe = new Regex("^(" + Lead + @")(\p{Ll})");
        private static readonly Regex CapAfterSentenceRe = new Regex("([.!?…])(" + Lead + @")(\p{Ll})");
        // PHP writes this capitalizer `/ui`, but PCRE2 does not fold a Unicode property, so its
        // `\p{Ll}` is still lower case only — only the tag NAME is caseless. JavaScript's `\p{Ll}`
        // under `i` takes every cased letter, and reading it that way turned a titlecase `ǅ` after
        // `<p>` into `Ǆ` where PHP keeps it (spintax-js#79). Scoping `i` to the tag names is the
        // whole of it; the `\p{Lt}` this class once carried reproduced the reference's own bug.
        private static readonly Regex CapAfterBlockRe = new Regex(@"(<\/?(?i:p|h[1-6]|li|blockquote|div|td|th)[^>]*>" + Lead + @")(\p{Ll})");
        private static readonly Regex CapAfterBreakRe = new Regex("(\n" + Lead + @")(\p{Ll})");

        // The shield's placeholder prefixes; RestoreRe is built from the same list so a new
        // shield pass cannot mint a key shape the restore fails to recognise.
        private const string ShieldPrefixes = "URL|URI|EMAIL|DOM|NUM|ABBR";
        private static readonly Regex RestoreRe = new Regex(@"\x00(?:" + ShieldPrefixes + @")_[0-9]+\x00");

        public static string PostProcess(string input)
        {
            var placeholders = new Placeholders();

            var text = input;

            // 1-5: shield. URIs first and in one pass; EMAIL/DOMAIN after, so a whole `mailto:`
            // survives instead of the address being carved out from under its prefix.
            text = UriRe.Replace(text, m => StoreWithTrailingPunct(placeholders, m.Value, MailTelPrefixRe.IsMatch(m.Value) ? "URI" : "URL"));
            text = EmailRe.Replace(text, m => placeholders.Store(m.Value, "EMAIL"));
            text = DomainRe.Replace(text, m => placeholders.Store(m.Value, "DOM"));
            text = DecimalRe.Replace(text, m => placeholders.Store(m.Value, "NUM"));
            text = MultiAbbrRe.Replace(text, m => placeholders.Store(m.Value, "ABBR"));
            text = SingleAbbrRe.Replace(text, m => placeholders.Store(m.Value, "ABBR"));

            // 6: collapse duplicate spaces/tabs.
            text = CollapseSpacesRe.Replace(text, " ");

            // 7: punctuation spacing — none before, one after ,;: and after a RUN of .!? unless a
            // digit / space / end / tag follows.
            text = SpaceBeforePunctRe.Replace(text, "$1");
            text = SpaceAfterCommaRe.Replace(text, "$1 ");
            text = SpaceAfterSentenceRe.Replace(text, "$1 ");
            // 7a: a Spanish opener binds to the word it opens.
            text = SpaceAfterOpenerRe.Replace(text, "$1");

            // 8-11: capitalise — first letter, after sentence punctuation, after block tags,
            // after line breaks — through tags and openers.
            text = CapFirstRe.Replace(text, m => m.Groups[1].Value + JsText.Upper(m.Groups[2].Value[0]));
            text = CapAfterSentenceRe.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + JsText.Upper(m.Groups[3].Value[0]));
            text = CapAfterBlockRe.Replace(text, m => m.Groups[1].Value + JsText.Upper(m.Groups[2].Value[0]));
            text = CapAfterBreakRe.Replace(text, m => m.Groups[1].Value + JsText.Upper(m.Groups[2].Value[0]));

            // 12: restore placeholders, then trim (JS semantics — see JsText).
            return JsText.Trim(Restore(text, input, placeholders));
        }

        /// <summary>
        /// Restore the shielded values. A single left-to-right token pass when the input carries
        /// no <c>\0</c>; the reference's per-key substring loop when it does. The two are NOT the
        /// same function (spintax-js#52/#54): the loop rewrites every occurrence of a key, pairs
        /// a caller's stray <c>\0</c> with a real delimiter, and is what the corpus pins for
        /// <c>\0</c>-carrying input; on <c>\0</c>-free input the single pass is both faster and
        /// the answer we want. Real text carries no <c>\0</c>, so the fast path is what runs.
        /// </summary>
        private static string Restore(string text, string input, Placeholders placeholders)
        {
            if (input.IndexOf('\0') < 0)
                return RestoreRe.Replace(text, m => placeholders.TryGet(m.Value, out var v) ? v : m.Value);

            var output = text;
            foreach (var (key, value) in placeholders.InOrder)
                output = output.Replace(key, value);
            return output;
        }

        /// <summary>A URI keeps its trailing sentence punctuation outside the placeholder.</summary>
        private static string StoreWithTrailingPunct(Placeholders placeholders, string value, string prefix)
        {
            var m = TrailingPunctRe.Match(value);
            if (!m.Success) return placeholders.Store(value, prefix);
            var suffix = m.Groups[1].Value;
            var body = value.Substring(0, value.Length - suffix.Length);
            return body.Length == 0 ? suffix : placeholders.Store(body, prefix) + suffix;
        }

        /// <summary>Per-call placeholder table: insertion-ordered (the loop restore depends on it) plus a lookup.</summary>
        private sealed class Placeholders
        {
            private readonly List<(string key, string value)> _order = new List<(string, string)>();
            private readonly Dictionary<string, string> _byKey = new Dictionary<string, string>(StringComparer.Ordinal);
            private int _counter;

            public string Store(string value, string prefix)
            {
                var key = "\0" + prefix + "_" + _counter + "\0";
                _order.Add((key, value));
                _byKey[key] = value;
                _counter++;
                return key;
            }

            public bool TryGet(string key, out string value) => _byKey.TryGetValue(key, out value!);

            public IEnumerable<(string key, string value)> InOrder => _order;
        }
    }
}
