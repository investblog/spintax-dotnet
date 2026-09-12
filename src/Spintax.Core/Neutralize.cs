using System.Text;

namespace Spintax.Core
{
    /// <summary>
    /// Port of <c>internal/neutralize.ts</c> — T2 shielding (spec §6). Structural chars
    /// <c>{ } [ ] % #</c> map to Private-Use-Area sentinels U+E000–U+E005 that no engine pass
    /// reads as markup; the mandatory <see cref="SafetyRestore"/> maps them back to literal
    /// glyphs at the very end of every render, post-process on or off.
    /// </summary>
    /// <remarks>
    /// Text-safe, not HTML-safe: <c>&lt; &gt; &amp;</c> are untouched, so this is not XSS
    /// mitigation. The PUA range is reserved: <c>Parser</c> strips stray sentinels from author
    /// markup (template source and include results) so that only <see cref="Neutralize"/> can
    /// introduce them.
    /// <para>
    /// The pipe is deliberately not in the set: <c>|</c> means nothing outside a construct.
    /// Inside one it does — a neutralized value the author places in <c>{…}</c> or <c>[…]</c> is
    /// still split on its <c>|</c>, in every engine: the reference expands before it reads a
    /// bracket, and this engine splices a direct reference the same way (spintax-dotnet#1).
    /// Shielding it would be a family-wide contract change.
    /// </para>
    /// </remarks>
    internal static class Shield
    {
        private const string Structural = "{}[]%#";
        private const char SentinelBase = '';
        private const char SentinelLast = '';

        /// <summary>Shield data-derived input so it cannot be re-read as spintax markup.</summary>
        public static string Neutralize(string value)
        {
            if (!ContainsAny(value, structural: true)) return value;
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                var i = Structural.IndexOf(ch);
                sb.Append(i < 0 ? ch : (char)(SentinelBase + i));
            }
            return sb.ToString();
        }

        /// <summary>Mandatory final stage: sentinels back to their literal glyphs.</summary>
        public static string SafetyRestore(string text)
        {
            if (!ContainsAny(text, structural: false)) return text;
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
                sb.Append(IsSentinel(ch) ? Structural[ch - SentinelBase] : ch);
            return sb.ToString();
        }

        /// <summary>Remove stray sentinels from author markup, so only Neutralize can mint them.</summary>
        public static string StripSentinels(string text)
        {
            if (!ContainsAny(text, structural: false)) return text;
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
                if (!IsSentinel(ch)) sb.Append(ch);
            return sb.ToString();
        }

        private static bool IsSentinel(char ch) => ch >= SentinelBase && ch <= SentinelLast;

        /// <summary>Fast path guard: structural chars (<c>true</c>) or sentinels (<c>false</c>) present?</summary>
        private static bool ContainsAny(string text, bool structural)
        {
            foreach (var ch in text)
            {
                if (structural ? Structural.IndexOf(ch) >= 0 : IsSentinel(ch)) return true;
            }
            return false;
        }
    }
}
