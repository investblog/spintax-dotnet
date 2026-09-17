namespace Spintax.Core
{
    /// <summary>
    /// The character classes of the reference engines' patterns, spelled out — the port of
    /// <c>internal/charclass.ts</c>. A pattern taken from PHP asks one question: does the PHP
    /// pattern carry <c>/u</c>? It takes <see cref="UcpSpaceChars"/> / <see cref="UcpWordChars"/>
    /// when it does, and <see cref="AsciiSpaceChars"/> / <see cref="AsciiWordBoundary"/> when it
    /// does not.
    /// </summary>
    /// <remarks>
    /// PHP compiles a <c>/u</c> pattern with PCRE2_UCP, which makes <c>\s</c>, <c>\d</c>, <c>\w</c>
    /// and <c>\b</c> Unicode (measured upstream on PHP 8.4.23 / PCRE2 10.44 — spintax-js#55, #81);
    /// a pattern without <c>/u</c> runs in byte mode, where the same shorthands are ASCII. .NET is
    /// a third dialect: its <c>\s</c> misses U+180E, its <c>\w</c> takes only <c>\p{Nd}</c> of the
    /// numbers, and its <c>\b</c> is built on that <c>\w</c>. So no shorthand is written here.
    /// <para>
    /// The post-process was once ported on the opposite belief ("no PCRE_UCP", written into the
    /// reference's own comments), and that is how <c>и т.д.</c> rendered <c>и т. Д.</c> and
    /// <c>пример.рф</c> rendered <c>пример. Рф</c> in every tree-walk engine while both PHP
    /// engines kept them intact.
    /// </para>
    /// <para>
    /// The escapes are written <c>"\\uXXXX"</c> in ordinary strings rather than raw characters in
    /// verbatim ones, deliberately: a raw U+2028 inside a literal is a line terminator to the C#
    /// compiler (CS1010), and a raw U+180E or U+205F is invisible in a diff.
    /// </para>
    /// </remarks>
    internal static class CharClass
    {
        /// <summary>
        /// PCRE2 UCP <c>\s</c> — <c>\p{Z}</c> plus <c>\h</c> and <c>\v</c>: U+0009–U+000D, U+0020,
        /// U+0085, U+00A0, U+1680, U+180E, U+2000–U+200A, U+2028, U+2029, U+202F, U+205F, U+3000.
        /// A fragment for inside <c>[…]</c>.
        /// </summary>
        public const string UcpSpaceChars =
            "\\t\\n\\x0B\\f\\r \\x85\\xA0\\u1680\\u180E\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000";

        /// <summary>The same set as a class.</summary>
        public const string UcpSpace = "[" + UcpSpaceChars + "]";

        /// <summary>
        /// PCRE2 UCP <c>\w</c> — letters, numbers, non-spacing marks and connector punctuation,
        /// <c>_</c> among them. PCRE2 before 10.43 leaves out the marks and every connector but
        /// <c>_</c>, so a PHP host on an older library differs next to one of those; the corpus
        /// measures PHP 8.4 (10.44) and this follows it. A fragment for inside <c>[…]</c>.
        /// </summary>
        public const string UcpWordChars = "\\p{L}\\p{N}\\p{Mn}\\p{Pc}";

        /// <summary>
        /// A leading UCP <c>\b</c> in front of a pattern that begins with a word character: the
        /// character before is not one. One lookbehind instead of two alternatives.
        /// </summary>
        public const string UcpAfterNonWord = "(?<![" + UcpWordChars + "])";

        /// <summary>
        /// UCP <c>\b</c> in general — a TLD can end in <c>-</c>, so the boundary after a domain
        /// can go either way.
        /// </summary>
        public const string UcpWordBoundary =
            "(?:(?<=[" + UcpWordChars + "])(?![" + UcpWordChars + "])|(?<![" + UcpWordChars + "])(?=[" + UcpWordChars + "]))";

        /// <summary>
        /// The whitespace of a pattern PHP writes WITHOUT <c>/u</c> — byte mode, so ASCII: the
        /// permutation config, in the parser and in the validator.
        /// </summary>
        public const string AsciiSpaceChars = " \\t\\n\\x0B\\f\\r";

        /// <summary>The same set as a class.</summary>
        public const string AsciiSpace = "[" + AsciiSpaceChars + "]";

        /// <summary>ASCII <c>\b</c>: a boundary between <c>[A-Za-z0-9_]</c> and anything else (start/end included).</summary>
        public const string AsciiWordBoundary =
            "(?:(?<![A-Za-z0-9_])(?=[A-Za-z0-9_])|(?<=[A-Za-z0-9_])(?![A-Za-z0-9_]))";

        /// <summary>
        /// <see cref="UcpSpaceChars"/> for a scanner that reads characters instead of running a
        /// regex — the conditional's truthiness test, which is PHP's <c>/\S/u</c>. Code points,
        /// not literals, for the reason in the remarks above.
        /// </summary>
        public static bool IsUcpSpace(char ch)
        {
            int code = ch;
            return (code >= 0x09 && code <= 0x0D)
                || code == 0x20
                || code == 0x85
                || code == 0xA0
                || code == 0x1680
                || code == 0x180E
                || (code >= 0x2000 && code <= 0x200A)
                || code == 0x2028
                || code == 0x2029
                || code == 0x202F
                || code == 0x205F
                || code == 0x3000;
        }
    }
}
