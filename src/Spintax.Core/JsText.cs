using System.Globalization;
using System.Text;

namespace Spintax.Core
{
    /// <summary>
    /// The two places where the port cannot call the BCL and stay byte-equal to the reference:
    /// JavaScript's <c>String.prototype.trim</c> and <c>toUpperCase</c>. Measured 2026-08-21
    /// (<c>docs/TODO.md</c>): .NET's <c>Trim()</c> keeps U+FEFF and removes U+0085, JS does the
    /// opposite; .NET's <c>ToUpperInvariant</c> maps <c>ß</c> to itself, JS to <c>SS</c>.
    /// </summary>
    internal static class JsText
    {
        /// <summary>
        /// JS <c>\s</c> as a .NET character class — the same set as <see cref="IsJsWhiteSpace"/>.
        /// .NET's own <c>\s</c> lacks U+FEFF and includes U+0085, so every reference regex that
        /// says <c>\s</c> is rewritten with this.
        /// </summary>
        public const string SChars = "\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF";

        public const string S = "[" + SChars + "]";

        /// <summary>JS LineTerminator set: LF, CR, LS, PS — what <c>^</c>/<c>$</c> see under the <c>m</c> flag and what <c>.</c> excludes.</summary>
        public const string LineTerminators = "\n\r\u2028\u2029";

        /// <summary>JS multiline <c>^</c>: start of input or right after a line terminator.</summary>
        public const string LineStart = "(?:^|(?<=[" + LineTerminators + "]))";

        /// <summary>JS multiline <c>$</c>: end of input or right before a line terminator. .NET's <c>$</c> knows only <c>\n</c>.</summary>
        public const string LineEnd = "(?=[" + LineTerminators + @"]|\z)";

        /// <summary>JS <c>.</c>: any char but a line terminator. .NET's <c>.</c> excludes only <c>\n</c>.</summary>
        public const string Dot = "[^" + LineTerminators + "]";

        /// <summary>
        /// ECMAScript WhiteSpace ∪ LineTerminator: TAB VT FF SP NBSP ZWNBSP, every
        /// <c>Zs</c> character, LF CR LS PS. NUL and NEL (U+0085) are NOT in the set.
        /// </summary>
        public static bool IsJsWhiteSpace(char ch)
        {
            switch (ch)
            {
                case '\t':
                case '\n':
                case '\v':
                case '\f':
                case '\r':
                case ' ':
                case '\u00A0':
                case '\uFEFF':
                case '\u2028':
                case '\u2029':
                    return true;
                default:
                    // Every other Zs (OGHAM SPACE MARK, EN QUAD … IDEOGRAPHIC SPACE) lies at or above U+1680.
                    return ch >= '\u1680' && CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.SpaceSeparator;
            }
        }

        /// <summary>JS <c>trim()</c>: strip <see cref="IsJsWhiteSpace"/> from both ends.</summary>
        public static string Trim(string s)
        {
            var start = 0;
            var end = s.Length;
            while (start < end && IsJsWhiteSpace(s[start])) start++;
            while (end > start && IsJsWhiteSpace(s[end - 1])) end--;
            return start == 0 && end == s.Length ? s : s.Substring(start, end - start);
        }

        /// <summary>
        /// JS <c>toUpperCase()</c> of ONE lowercase letter. Full Unicode case mapping expands a
        /// handful of letters to several (SpecialCasing.txt, unconditional mappings); the BCL
        /// only knows the one-to-one simple mapping, so those 75 BMP letters are listed here —
        /// generated from Unicode 15.1, not typed by hand. Everything else is the simple
        /// invariant mapping.
        /// </summary>
        public static string Upper(char ch)
        {
            var multi = MultiUpper(ch);
            return multi ?? char.ToUpperInvariant(ch).ToString();
        }

        private static string? MultiUpper(char ch)
        {
            switch (ch)
            {
                case 'ß': return "SS"; // ß -> SS
                case 'ŉ': return "ʼN"; // ŉ -> ʼN
                case 'ǰ': return "J\u030C"; // ǰ -> J̌
                case 'ΐ': return "Ι\u0308\u0301"; // ΐ -> Ϊ́
                case 'ΰ': return "Υ\u0308\u0301"; // ΰ -> Ϋ́
                case 'և': return "ԵՒ"; // և -> ԵՒ
                case 'ẖ': return "H\u0331"; // ẖ -> H̱
                case 'ẗ': return "T\u0308"; // ẗ -> T̈
                case 'ẘ': return "W\u030A"; // ẘ -> W̊
                case 'ẙ': return "Y\u030A"; // ẙ -> Y̊
                case 'ẚ': return "Aʾ"; // ẚ -> Aʾ
                case 'ὐ': return "Υ\u0313"; // ὐ -> Υ̓
                case 'ὒ': return "Υ\u0313\u0300"; // ὒ -> Υ̓̀
                case 'ὔ': return "Υ\u0313\u0301"; // ὔ -> Υ̓́
                case 'ὖ': return "Υ\u0313\u0342"; // ὖ -> Υ̓͂
                case 'ᾀ': return "ἈΙ"; // ᾀ -> ἈΙ
                case 'ᾁ': return "ἉΙ"; // ᾁ -> ἉΙ
                case 'ᾂ': return "ἊΙ"; // ᾂ -> ἊΙ
                case 'ᾃ': return "ἋΙ"; // ᾃ -> ἋΙ
                case 'ᾄ': return "ἌΙ"; // ᾄ -> ἌΙ
                case 'ᾅ': return "ἍΙ"; // ᾅ -> ἍΙ
                case 'ᾆ': return "ἎΙ"; // ᾆ -> ἎΙ
                case 'ᾇ': return "ἏΙ"; // ᾇ -> ἏΙ
                case 'ᾐ': return "ἨΙ"; // ᾐ -> ἨΙ
                case 'ᾑ': return "ἩΙ"; // ᾑ -> ἩΙ
                case 'ᾒ': return "ἪΙ"; // ᾒ -> ἪΙ
                case 'ᾓ': return "ἫΙ"; // ᾓ -> ἫΙ
                case 'ᾔ': return "ἬΙ"; // ᾔ -> ἬΙ
                case 'ᾕ': return "ἭΙ"; // ᾕ -> ἭΙ
                case 'ᾖ': return "ἮΙ"; // ᾖ -> ἮΙ
                case 'ᾗ': return "ἯΙ"; // ᾗ -> ἯΙ
                case 'ᾠ': return "ὨΙ"; // ᾠ -> ὨΙ
                case 'ᾡ': return "ὩΙ"; // ᾡ -> ὩΙ
                case 'ᾢ': return "ὪΙ"; // ᾢ -> ὪΙ
                case 'ᾣ': return "ὫΙ"; // ᾣ -> ὫΙ
                case 'ᾤ': return "ὬΙ"; // ᾤ -> ὬΙ
                case 'ᾥ': return "ὭΙ"; // ᾥ -> ὭΙ
                case 'ᾦ': return "ὮΙ"; // ᾦ -> ὮΙ
                case 'ᾧ': return "ὯΙ"; // ᾧ -> ὯΙ
                case 'ᾲ': return "ᾺΙ"; // ᾲ -> ᾺΙ
                case 'ᾳ': return "ΑΙ"; // ᾳ -> ΑΙ
                case 'ᾴ': return "ΆΙ"; // ᾴ -> ΆΙ
                case 'ᾶ': return "Α\u0342"; // ᾶ -> Α͂
                case 'ᾷ': return "Α\u0342Ι"; // ᾷ -> Α͂Ι
                case 'ῂ': return "ῊΙ"; // ῂ -> ῊΙ
                case 'ῃ': return "ΗΙ"; // ῃ -> ΗΙ
                case 'ῄ': return "ΉΙ"; // ῄ -> ΉΙ
                case 'ῆ': return "Η\u0342"; // ῆ -> Η͂
                case 'ῇ': return "Η\u0342Ι"; // ῇ -> Η͂Ι
                case 'ῒ': return "Ι\u0308\u0300"; // ῒ -> Ϊ̀
                case 'ΐ': return "Ι\u0308\u0301"; // ΐ -> Ϊ́
                case 'ῖ': return "Ι\u0342"; // ῖ -> Ι͂
                case 'ῗ': return "Ι\u0308\u0342"; // ῗ -> Ϊ͂
                case 'ῢ': return "Υ\u0308\u0300"; // ῢ -> Ϋ̀
                case 'ΰ': return "Υ\u0308\u0301"; // ΰ -> Ϋ́
                case 'ῤ': return "Ρ\u0313"; // ῤ -> Ρ̓
                case 'ῦ': return "Υ\u0342"; // ῦ -> Υ͂
                case 'ῧ': return "Υ\u0308\u0342"; // ῧ -> Ϋ͂
                case 'ῲ': return "ῺΙ"; // ῲ -> ῺΙ
                case 'ῳ': return "ΩΙ"; // ῳ -> ΩΙ
                case 'ῴ': return "ΏΙ"; // ῴ -> ΏΙ
                case 'ῶ': return "Ω\u0342"; // ῶ -> Ω͂
                case 'ῷ': return "Ω\u0342Ι"; // ῷ -> Ω͂Ι
                case 'ﬀ': return "FF"; // ﬀ -> FF
                case 'ﬁ': return "FI"; // ﬁ -> FI
                case 'ﬂ': return "FL"; // ﬂ -> FL
                case 'ﬃ': return "FFI"; // ﬃ -> FFI
                case 'ﬄ': return "FFL"; // ﬄ -> FFL
                case 'ﬅ': return "ST"; // ﬅ -> ST
                case 'ﬆ': return "ST"; // ﬆ -> ST
                case 'ﬓ': return "ՄՆ"; // ﬓ -> ՄՆ
                case 'ﬔ': return "ՄԵ"; // ﬔ -> ՄԵ
                case 'ﬕ': return "ՄԻ"; // ﬕ -> ՄԻ
                case 'ﬖ': return "ՎՆ"; // ﬖ -> ՎՆ
                case 'ﬗ': return "ՄԽ"; // ﬗ -> ՄԽ
                default: return null;
            }
        }

        /// <summary>Append with JS <c>toUpperCase</c> semantics for one char.</summary>
        public static StringBuilder AppendUpper(this StringBuilder sb, char ch) => sb.Append(Upper(ch));
    }
}
