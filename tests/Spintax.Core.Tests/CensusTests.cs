using System.Collections.Generic;
using System.Linq;
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

        [Theory]
        // An element that can render blank is dropped, with the separator it carried, and the size
        // clamp and the shuffle count what is left (spintax-js#80). The count follows the draw:
        // 3! ways when {b|} picks b, 2! when it picks nothing — 8, not the 3!·2 = 12 the element
        // list alone says. MaxLength is measured on the full list, where the longest render is.
        [InlineData("[a|{b|}|c]", 8, 5)]
        [InlineData("[<minsize=3;maxsize=3>a|{b|}|c]", 8, 5)]
        [InlineData("[a<1>|{x|}<2>|b]", 8, 5)]
        // Two droppable elements, and the all-blank draw, which renders "" — one outcome, not none.
        [InlineData("[{a|}|{b|}]", 5, 3)]
        public void A_droppable_element_is_counted_as_the_renderer_draws_it(string template, long combos, long maxLen)
        {
            Assert.Equal(combos, Engine.Combinations(template));
            Assert.Equal(maxLen, Engine.MaxLength(template));
            Assert.Equal(combos, DistinctRenders(template));
        }

        [Fact]
        public void A_dropped_element_narrows_the_size_range_the_count_uses()
        {
            // Nine paths while {b|} draws b — three singles and six ordered pairs — and four more
            // when it draws nothing, because the clamp is then 1..2 of TWO survivors. Thirteen
            // paths over nine distinct texts: "a" and "a c" are drawn in both readings, and this
            // walk counts paths, as it counts {a|a} as 2 (see the class remarks).
            Assert.Equal(13, Engine.Combinations("[<minsize=1;maxsize=2>a|{b|}|c]"));
            Assert.Equal(9, DistinctRenders("[<minsize=1;maxsize=2>a|{b|}|c]"));
            Assert.Equal(3, Engine.MaxLength("[<minsize=1;maxsize=2>a|{b|}|c]")); // "a b"
        }

        [Fact]
        public void A_permutation_of_elements_that_never_blank_counts_exactly_as_it_did()
        {
            // The dropping walk is the old Σ k!·e_k when nothing can be dropped — the ordinary
            // template, and the property that keeps this change from moving any other number.
            Assert.Equal(6, Engine.Combinations("[a|b|c]"));
            Assert.Equal(9, Engine.Combinations("[<minsize=1;maxsize=2>a|b|c]"));
            Assert.Equal(4, Engine.Combinations("[{a|b}|c]"));
        }

        /// <summary>
        /// Every draw of a template whose outputs are all different, enumerated through the RNG
        /// seam — the same brute force as <see cref="The_count_matches_an_exhaustive_enumeration_of_picks"/>.
        /// </summary>
        private static int DistinctRenders(string template)
        {
            var seen = new HashSet<string>();
            for (var trial = 0; trial < 20000; trial++)
            {
                var seq = new int[8];
                var s = trial;
                for (var i = 0; i < seq.Length; i++) { seq[i] = s % 3; s /= 3; }
                var k = 0;
                Rng rng = (min, max) => System.Math.Max(min, System.Math.Min(max, seq[System.Math.Min(k++, seq.Length - 1)]));
                seen.Add(Engine.RenderWith(template, rng, new RenderOptions { PostProcess = false }));
            }
            return seen.Count;
        }

        // ── the exhaustion paths: what the count says where the render stops expanding ──────
        // Each of these reported a number the engine contradicted until 2026-09-12, and each was
        // measured against the render before and after (docs/TODO.md, the Codex gate).

        [Fact]
        public void A_template_past_the_expansion_allowance_saturates_instead_of_understating()
        {
            // The render spends the whole allowance on X and leaves %L% literal. Charging nothing,
            // the census spliced %L% into two options and reported a length three characters SHORT
            // of the render — understating, the half a host acts on. It cannot say which draw was
            // cut either, since mutually exclusive branches each deserve what the other spent, so
            // it stops claiming a number.
            var row = Row(("X", new string('a', 1024 * 1024)), ("L", "a|b"));
            var rendered = Engine.Render("%X%{%L%}", new RenderOptions { Context = row, PostProcess = false, Seed = "1" });
            Assert.EndsWith("%L%", rendered);
            Assert.Equal(long.MaxValue, Engine.Combinations("%X%{%L%}", row));
            Assert.Equal(long.MaxValue, Engine.MaxLength("%X%{%L%}", row));
            Assert.True(Engine.MaxLength("%X%{%L%}", row) >= rendered.Length, "a length must never fall short of a render");

            // Inside the allowance it is still exact, which is every ordinary template.
            var small = Row(("X", "ab"), ("L", "a|b"));
            Assert.Equal(2, Engine.Combinations("%X%{%L%}", small));
            Assert.Equal(3, Engine.MaxLength("%X%{%L%}", small));
        }

        [Fact]
        public void A_plural_slot_and_a_branch_past_the_allowance_saturate_too()
        {
            var mib = new string('a', 1024 * 1024);

            // The allowance is charged in the plural slots as well, or a walk that spent it
            // elsewhere would call an understated length exact.
            var slot = Row(("X", mib), ("S", "a"));
            var rendered = Engine.Render("%X%{plural 1: %S%|b}", new RenderOptions { Context = slot, PostProcess = false, Locale = "en", Seed = "1" });
            Assert.EndsWith("%S%", rendered);
            Assert.Equal(long.MaxValue, Engine.MaxLength("%X%{plural 1: %S%|b}", slot, "en"));

            // Mutually exclusive branches: a render takes one and spends the whole allowance on it,
            // so neither branch can be judged with what the other left. Counting A first and then
            // calling B one literal path said 2 where a render of B alone has 3.
            Assert.Equal(long.MaxValue, Engine.Combinations("{{%A%}|{%B%}}", Row(("A", mib), ("B", "{x|y}"))));
            Assert.Equal(long.MaxValue, Engine.MaxLength("{{%A%}|{%B%}}", Row(("A", mib), ("B", new string('b', 2 * 1024 * 1024)))));
        }

        [Fact]
        public void A_definition_chain_past_the_structural_backstop_saturates_without_overflowing()
        {
            // The backstop is this walk's own, not the renderer's, and the walk is recursive: at
            // 1000 a 1100-long chain overflowed the stack. It sits at 200 and saturates beyond.
            var defs = string.Join("\n", Enumerable.Range(1, 1100).Select(i => "#def %d" + i + "% = %d" + (i + 1) + "%"));
            Assert.Equal(long.MaxValue, Engine.MaxLength(defs + "\n#def %d1101% = END\n%d1%"));
        }

        [Fact]
        public void A_definition_chain_longer_than_the_variable_cap_is_rolled_in_full()
        {
            // The renderer caps VARIABLE hops at 50; rolling a definition is not one, so a 55-long
            // alias chain reaches END. Counting every descent against that cap returned 0 here.
            var defs = string.Join("\n", Enumerable.Range(1, 55).Select(i => "#def %d" + i + "% = %d" + (i + 1) + "%"));
            var t = defs + "\n#def %d56% = END\n%d1%";
            Assert.Equal("\n\nEND", Engine.Render(t, new RenderOptions { PostProcess = false, Seed = "1" }));
            Assert.Equal(5, Engine.MaxLength(t));
        }

        [Fact]
        public void A_construct_free_definition_splices_into_a_construct_as_the_render_splices_it()
        {
            // A #def is rolled once and held, so when its value carries no construct the rolled
            // text is the value and the census knows what the renderer splices.
            Assert.Equal(3, Engine.Combinations("#def %L% = a|b|c\n{%L%}"));
            Assert.Equal(2, Engine.MaxLength("#def %L% = a|b|c\n{%L%}")); // "\n" + one option
        }

        [Fact]
        public void A_doubling_macro_ends_at_the_allowance_instead_of_walking_2_to_the_50()
        {
            // #set %a% = %b% %b% over #set %b% = %a% %a% doubles the tree at every level: the depth
            // cap bounds the height, not the width, so this used to visit 2^50 nodes and the
            // process died. The allowance the renderer charges bounds the work here as well.
            const string bomb = "#set %a% = %b% %b%\n#set %b% = %a% %a%\n{%a%}";
            var started = System.Diagnostics.Stopwatch.StartNew();
            Assert.Equal(long.MaxValue, Engine.Combinations(bomb));
            Assert.Equal(long.MaxValue, Engine.MaxLength(bomb));
            Assert.True(started.ElapsedMilliseconds < 60_000, "the census must end at the allowance, not run away");
        }
    }
}
