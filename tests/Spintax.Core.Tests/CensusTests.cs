using System.Collections.Generic;
using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>Combinations / MaxLength: one construct at a time, then the interactions, then a brute-force cross-check.</summary>
    public class CensusTests
    {
        private static Dictionary<string, string> Row(params (string k, string v)[] pairs)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        [Theory]
        [InlineData("plain", 1, 5)]
        [InlineData("{a|bb|ccc}", 3, 3)]
        [InlineData("{a|b} {c|d|e}", 6, 3)]
        [InlineData("{a|{b|c}}", 3, 1)]
        [InlineData("{|brand new }x", 2, 11)]
        [InlineData("[a|b|c]", 6, 5)]                                     // 3! orderings, "a b c"
        [InlineData("[<minsize=1;maxsize=2>a|b|c]", 9, 3)]               // 3 singles + 6 ordered pairs
        [InlineData("[<minsize=2;maxsize=3;sep=\", \";lastsep=\" и \">aa|b|c]", 12, 9)] // 6 pairs + 6 triples; "aa, b и c"
        [InlineData("[{a|b}|c]", 4, 3)]                                   // k=2: 2!·(2·1)=4
        [InlineData("{?x?yes|no}", 2, 3)]
        [InlineData("{plural 2: one|many}", 2, 4)]                           // no locale = two forms; a third would be plural.arity, verbatim
        public void Each_construct_across_all_data(string template, long combos, long maxLen)
        {
            Assert.Equal(combos, Engine.Combinations(template));
            Assert.Equal(maxLen, Engine.MaxLength(template));
        }

        [Fact]
        public void Def_rolls_once_set_rerolls_each_time()
        {
            Assert.Equal(3, Engine.Combinations("#def %a% = {x|y|z}\n%a% %a%"));
            Assert.Equal(9, Engine.Combinations("#set %a% = {x|y|z}\n%a% %a%"));
            Assert.Equal(1, Engine.Combinations("#def %a% = {x|y|z}\nno reference"));
            Assert.Equal(4, Engine.MaxLength("#def %a% = {x|y|z}\n%a% %a%")); // "\nx x" — the stripped directive leaves its newline
        }

        [Fact]
        public void A_row_resolves_conditionals_plurals_and_variable_lengths()
        {
            const string t = "%product%: {?sale?{now|today} cheap|full price} {plural %n%: item|items}";
            Assert.Equal(6, Engine.Combinations(t));
            Assert.Equal(2, Engine.Combinations(t, Row(("product", "Ferra"), ("sale", "yes"), ("n", "5")), "en"));
            Assert.Equal(1, Engine.Combinations(t, Row(("product", "Ferra"), ("sale", ""), ("n", "1")), "en"));
            // Longest for the row: "Ferra: today cheap items"
            Assert.Equal("Ferra: today cheap items".Length, Engine.MaxLength(t, Row(("product", "Ferra"), ("sale", "yes"), ("n", "5")), "en"));
        }

        [Fact]
        public void Russian_plural_for_a_row_takes_the_bucket_the_count_takes()
        {
            // Variety inside a form arrives through a #def (a {…} written in the slot is
            // plural.nested-brackets and renders as the fullwidth fallback — one outcome).
            const string t = "#def %f% = {штука|единица}\n{plural %n%: %f%|штуки|штук}";
            Assert.Equal(2, Engine.Combinations(t, Row(("n", "21")), "ru")); // the "one" form references the def: ×2
            Assert.Equal(1, Engine.Combinations(t, Row(("n", "5")), "ru"));  // "штук" does not
            Assert.Equal(4, Engine.Combinations(t, null, "ru"));             // (1 + 1 + 1) forms, ×2 for the def — distinct outputs: 2 + 1 + 1
            Assert.Equal(1, Engine.Combinations("{plural %n%: {a|b}|c|d}", Row(("n", "1")), "ru"));
        }

        [Fact]
        public void A_def_that_references_a_def_shares_its_roll()
        {
            Assert.Equal(2, Engine.Combinations("#def %b% = {x|y}\n#def %a% = %b%\n%a% %b%"));          // x x, y y
            Assert.Equal(4, Engine.Combinations("#def %b% = {x|y}\n#def %a% = {%b% p|%b% q}\n%a% %b%")); // a's own 2 × b's 2
            Assert.Equal(3, Engine.Combinations("#def %b% = {x|y}\n#def %a% = {%b%|z}\n%a%"));           // x, y, z
            Assert.Equal(1, Engine.Combinations("#def %a% = %a%\n%a%"));                                // a cycle rolls once
        }

        [Fact]
        public void Inverted_conditional_follows_the_row()
        {
            Assert.Equal(1, Engine.Combinations("{?!sale?{a|b}|c}", Row(("sale", "yes"))));
            Assert.Equal(2, Engine.Combinations("{?!sale?{a|b}|c}", Row(("sale", " "))));
        }

        [Fact]
        public void A_row_value_with_constructs_is_reparsed_as_the_renderer_does()
        {
            Assert.Equal(2, Engine.Combinations("%x%", Row(("x", "{a|b}"))));
            Assert.Equal(2, Engine.MaxLength("%x%", Row(("x", "{a|bb}"))));
            Assert.Equal(1, Engine.Combinations("%x%", Row(("x", "plain"))));
        }

        [Fact]
        public void Options_that_spell_the_same_count_as_paths()
        {
            Assert.Equal(2, Engine.Combinations("{a|a}"));
        }

        [Fact]
        public void Plural_mirrors_the_renderer_error_paths()
        {
            Assert.Equal(1, Engine.Combinations("{plural 2: one|many}", null, "ru"));                       // wrong arity: verbatim
            Assert.Equal("{plural 2: one|many}".Length, Engine.MaxLength("{plural 2: one|many}", null, "ru"));
            Assert.Equal(0, Engine.MaxLength("{plural %n%: a|b|c}", Row(("n", "+1")), "ru"));                // non-numeric: erased
            Assert.Equal(1, Engine.Combinations("{plural %n%: a|b|c}", Row(("n", "+1")), "ru"));
            Assert.Equal(1, Engine.Combinations("{plural {?x?1|2}: a|bb|c}", Row(("x", "")), "ru"));           // count conditional → 2 → few
            Assert.Equal(2, Engine.MaxLength("{plural {?x?1|2}: a|bb|c}", Row(("x", "")), "ru"));
            Assert.Equal(1, Engine.MaxLength("{plural {?x?1|2}: a|bb|c}", Row(("x", "y")), "ru"));             // → 1 → one
            Assert.Equal(3, Engine.Combinations("#def %n% = {1|2}\n{plural %n%: a|b|c}", Row(("x", "")), "ru")); // roll-driven count: every form
        }

        [Fact]
        public void Counts_saturate_instead_of_overflowing()
        {
            var t = string.Concat(System.Linq.Enumerable.Repeat("{a|b|c|d|e|f|g|h|i|j}", 25)); // 10^25
            Assert.Equal(long.MaxValue, Engine.Combinations(t));
        }

        [Fact]
        public void The_count_matches_an_exhaustive_enumeration_of_picks()
        {
            // Every combination of choices, enumerated through the RNG seam: the distinct outputs of
            // a template with all-different options must equal the census (small enough to walk).
            const string t = "{a|b}[<minsize=1;maxsize=2>x|y|z]{c|d}";
            var census = Engine.Combinations(t);
            var seen = new HashSet<string>();
            for (var trial = 0; trial < 20000; trial++)
            {
                var seq = new int[8];
                var s = trial;
                for (var i = 0; i < seq.Length; i++) { seq[i] = s % 3; s /= 3; }
                var k = 0;
                Rng rng = (min, max) => System.Math.Max(min, System.Math.Min(max, seq[System.Math.Min(k++, seq.Length - 1)]));
                seen.Add(Engine.RenderWith(t, rng, new RenderOptions { PostProcess = false }));
            }
            Assert.Equal(census, seen.Count);
        }
    }
}
