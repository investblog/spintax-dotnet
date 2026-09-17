using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

        // The email and bare-domain shields are the plugin's patterns — `[a-z0-9._%+\-]+@DOMAIN\b`
        // and `\bDOMAIN\b` — run by a SCANNER instead of a global replace, because the replace
        // retries from every start inside a long run: one 131 000-letter word, and a dotted run
        // `a.a.a.…`, which the one-case TLD above turned from one match into a chain retried from
        // every label (2000 labels: 0.2 ms as a match, 149 ms as a retry). The scanner tries the
        // same regex at the same starts, in the same order, and skips only starts that provably
        // fail. Both PHP engines do the same with `(*SKIP)(*FAIL)`; .NET has no backtracking verb.
        //
        // Email: every start inside one run of local-part characters reaches the same end — the
        // class holds no `@` — so the run's first start matches or none does, and a failed run is
        // skipped whole. That makes the `@` the thing to look for: the only run that can match is
        // the one ending at it.
        private static readonly Regex DomainAtRe = new Regex(@"\G" + DomainPart + B);

        // Domain: an attempt that fails at the start of a chain of labels (`a.b-c.d…`) fails at
        // every later start in that chain too — prefix the chain's own labels to a match further
        // in and it is a match here. So a failed attempt skips to where the chain ends. One regex
        // does all of it: it matches at exactly the starts the shield tries (a word character not
        // preceded by one — every label begins with one), group 1 is a domain, and when there is
        // none the whole match is the chain to skip.
        private static readonly Regex DomainScanRe =
            new Regex(AfterNonWord + "(?:(" + DomainPart + B + ")|(?:" + Label + @"\.)*" + Label + ")");

        /// <summary>Every domain holds a dot followed by the first character of a label; most prose holds none.</summary>
        private static readonly Regex DomainDotRe = new Regex(@"\.[\p{L}\p{N}]");
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

        // The capitalizers after a sentence end, a block tag and a line break — the plugin's
        // `([.!?…])(LEAD)(\p{Ll})`, `(<\/?(?:p|h[1-6]|li|blockquote|div|td|th)[^>]*>LEAD)(\p{Ll})`
        // (caseless) and `(\nLEAD)(\p{Ll})` — run by a scanner, because the regexes rescanned the
        // lead from every start: 20 000 of `\n` plus a space took 11.1 s here, and 20 000 `<p>`
        // with no letter after them 7.4 s.
        //
        // What makes a scanner exact is that the lead has one reading. An opener or a whitespace
        // character is a token of one character; a tag is `<`, at least one character that is not
        // `>`, then the FIRST `>` — `[^>]+` cannot cross a `>`, so a tag ends where the next `>`
        // is, and a `<` followed at once by `>`, or by no `>` at all, is no tag. Every shorter run
        // of tokens ends before a `<`, an opener or a space, none of which is `\p{Ll}`, so a start
        // matches exactly when the character after its LONGEST lead is a lowercase letter.
        //
        // PHP writes the block-tag pass `/ui`, but PCRE2 does not fold a Unicode property, so its
        // `\p{Ll}` is still lower case only — only the tag NAME is caseless. JavaScript's `\p{Ll}`
        // under `i` takes every cased letter, and reading it that way turned a titlecase `ǅ` after
        // `<p>` into `Ǆ` where PHP keeps it (spintax-js#79).
        private static readonly Regex BlockTagNameRe = new Regex(@"\G<\/?(?:p|h[1-6]|li|blockquote|div|td|th)", Ci);

        /// <summary>`.`, `!`, `?` and `…` — the boundaries of the sentence capitalizer.</summary>
        private const string SentenceEnds = ".!?…";

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
            text = ShieldEmails(text, v => placeholders.Store(v, "EMAIL"));
            text = ShieldDomains(text, v => placeholders.Store(v, "DOM"));
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
            var leads = new LeadIndexCache();
            text = CapitalizeAfter(text, AfterSentenceEnd, leads);
            text = CapitalizeAfter(text, AfterBlockTag(), leads);
            text = CapitalizeAfter(text, AfterLineBreak, leads);

            // 12: restore placeholders, then trim (JS semantics — see JsText).
            return JsText.Trim(Restore(text, input, placeholders));
        }

        /// <summary>
        /// Where the next boundary of a capitalizer pass is: where the lead after it starts, and
        /// where to resume searching. <c>false</c> ⇒ no boundary left.
        /// </summary>
        private delegate bool NextBoundary(string text, int from, out int leadStart, out int resume);

        /// <summary>
        /// <c>leadEnd[i]</c>: where the lead that starts at <c>i</c> ends — indexed from the right,
        /// once per text, when a lead first needs it. Walking every lead in full would read a run
        /// of line breaks once per break: each one starts a lead that holds the rest.
        /// </summary>
        private sealed class LeadIndexCache
        {
            private string? _text;
            private int[]? _leadEnd;

            public int[] For(string text)
            {
                // The passes only change the case of letters, and no upper-case mapping is shorter
                // than its letter, so a text of the same length has every `<`, `>`, opener and
                // space where the index saw them: one index serves all three passes unless a
                // letter grew (`ß` → `SS`).
                if (_leadEnd != null && _text != null && _text.Length == text.Length) return _leadEnd;
                _text = text;
                _leadEnd = Build(text);
                return _leadEnd;
            }

            private static int[] Build(string text)
            {
                var n = text.Length;
                var leadEnd = new int[n + 1];
                leadEnd[n] = n;
                var gt = -1; // the first `>` after the position being indexed
                for (var i = n - 1; i >= 0; i--)
                {
                    var ch = text[i];
                    var tokenEnd = -1;
                    if (SentenceOpeners.IndexOf(ch) >= 0 || CharClass.IsUcpSpace(ch)) tokenEnd = i + 1;
                    else if (ch == '<' && gt > i + 1) tokenEnd = gt + 1;
                    leadEnd[i] = tokenEnd == -1 ? i : leadEnd[tokenEnd];
                    if (ch == '>') gt = i;
                }
                return leadEnd;
            }
        }

        /// <summary>Lead steps walked one character at a time before the index is built.</summary>
        private const int LeadWalk = 32;

        /// <summary>
        /// Where the lead starting at <paramref name="i"/> ends. Openers and whitespace are one
        /// character each, so a short lead is walked; a tag, or a lead longer than
        /// <see cref="LeadWalk"/>, is answered by the index.
        /// </summary>
        private static int LeadEndFrom(string text, int i, LeadIndexCache leads)
        {
            var j = i;
            for (var steps = 0; steps < LeadWalk && j < text.Length; steps++)
            {
                var ch = text[j];
                if (ch == '<') return leads.For(text)[j];
                if (SentenceOpeners.IndexOf(ch) < 0 && !CharClass.IsUcpSpace(ch)) return j;
                j++;
            }
            return j < text.Length ? leads.For(text)[j] : j;
        }

        /// <summary>
        /// One capitalizer pass: for each boundary <paramref name="next"/> finds, upper-case the
        /// <c>\p{Ll}</c> at the end of the lead after it. After a match the search resumes behind
        /// the letter, as a global replace does.
        /// </summary>
        private static string CapitalizeAfter(string text, NextBoundary next, LeadIndexCache leads)
        {
            StringBuilder? output = null;
            var emitted = 0;
            var from = 0;
            while (next(text, from, out var leadStart, out var resume))
            {
                from = resume;
                var at = LeadEndFrom(text, leadStart, leads);
                if (at >= text.Length) continue;
                var ch = text[at];
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.LowercaseLetter) continue;
                output ??= new StringBuilder();
                output.Append(text, emitted, at - emitted).Append(JsText.Upper(ch));
                emitted = at + 1;
                from = emitted;
            }
            return output is null ? text : output.Append(text, emitted, text.Length - emitted).ToString();
        }

        private static bool AfterSentenceEnd(string text, int from, out int leadStart, out int resume)
        {
            leadStart = resume = 0;
            for (var i = from; i < text.Length; i++)
            {
                if (SentenceEnds.IndexOf(text[i]) < 0) continue;
                leadStart = resume = i + 1;
                return true;
            }
            return false;
        }

        /// <summary>The block-tag pass asks for the first <c>&gt;</c> after each tag name, at positions that only grow: one scan.</summary>
        private static NextBoundary AfterBlockTag()
        {
            var gtFrom = -1;
            var gt = -1;
            return (string text, int from, out int leadStart, out int resume) =>
            {
                leadStart = resume = 0;
                for (var lt = text.IndexOf('<', from); lt >= 0; lt = text.IndexOf('<', lt + 1))
                {
                    var m = BlockTagNameRe.Match(text, lt);
                    if (!m.Success) continue;
                    var nameEnd = m.Index + m.Length;
                    if (gtFrom == -1 || nameEnd < gtFrom || (gt != -1 && nameEnd > gt))
                    {
                        gtFrom = nameEnd;
                        gt = text.IndexOf('>', nameEnd);
                    }
                    if (gt == -1) continue;
                    leadStart = gt + 1;
                    resume = lt + 1;
                    return true;
                }
                return false;
            };
        }

        private static bool AfterLineBreak(string text, int from, out int leadStart, out int resume)
        {
            var at = from >= text.Length ? -1 : text.IndexOf('\n', from);
            leadStart = resume = at + 1;
            return at >= 0;
        }

        /// <summary>
        /// <c>[a-z0-9._%+-]</c> as the plugin's pattern reads it under <c>iu</c>: both cases, and
        /// the two non-ASCII letters that fold into the class.
        /// </summary>
        private static bool IsEmailLocalChar(char ch) =>
            (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')
            || ch == '.' || ch == '_' || ch == '%' || ch == '+' || ch == '-'
            || ch == 'ſ' || ch == 'K';

        /// <summary>The email shield, as a scanner over the <c>@</c>s — see <see cref="DomainAtRe"/>.</summary>
        private static string ShieldEmails(string text, Func<string, string> shield)
        {
            StringBuilder? output = null;
            var emitted = 0;
            // Where the run search may resume: after the last shield, and past every `@` tried.
            var resume = 0;
            for (var at = text.IndexOf('@'); at >= 0; at = text.IndexOf('@', Math.Max(at + 1, resume)))
            {
                // The run of local-part characters that ends at this `@`, begun no earlier than
                // the scan could begin it.
                var start = at;
                while (start > resume && IsEmailLocalChar(text[start - 1])) start--;
                if (start == at) continue; // no run ends here, so no attempt is made at it
                var m = DomainAtRe.Match(text, at + 1);
                if (!m.Success) continue;
                var end = m.Index + m.Length;
                output ??= new StringBuilder();
                output.Append(text, emitted, start - emitted).Append(shield(text.Substring(start, end - start)));
                emitted = resume = end;
            }
            return output is null ? text : output.Append(text, emitted, text.Length - emitted).ToString();
        }

        /// <summary>The bare-domain shield, as a scanner over the label chains — see <see cref="DomainScanRe"/>.</summary>
        private static string ShieldDomains(string text, Func<string, string> shield)
        {
            if (!DomainDotRe.IsMatch(text)) return text;
            StringBuilder? output = null;
            var emitted = 0;
            for (var m = DomainScanRe.Match(text); m.Success; m = m.NextMatch())
            {
                var domain = m.Groups[1];
                if (!domain.Success) continue;
                output ??= new StringBuilder();
                output.Append(text, emitted, domain.Index - emitted).Append(shield(domain.Value));
                emitted = domain.Index + domain.Length;
            }
            return output is null ? text : output.Append(text, emitted, text.Length - emitted).ToString();
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
