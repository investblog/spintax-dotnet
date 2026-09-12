using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// A direct <c>%var%</c> is spliced as TEXT before the construct is split (spintax-dotnet#1,
    /// the reference's 0.7.0). The corpus pins the outputs (<c>splice/*</c>, 19 cases); these
    /// pin what it cannot: the bomb and the depth bounds through the public entry, the parser's
    /// <c>Raw</c> marking, and the seams the reference's review found.
    /// </summary>
    public class SpliceTests
    {
        private static Rng First => (min, _) => min;

        private static Rng Last => (_, max) => max;

        /// <summary>Each value is a RAW pick, clamped into [min, max]; the last one repeats.</summary>
        private static Rng Sequence(params int[] seq)
        {
            var i = 0;
            return (min, max) => Math.Max(min, Math.Min(max, seq[Math.Min(i++, seq.Length - 1)]));
        }

        private static string Render(string template, Rng rng, IReadOnlyDictionary<string, string>? context = null, string? locale = null)
        {
            var o = new RenderOptions { PostProcess = false, Context = context, Locale = locale };
            return Engine.RenderWith(template, rng, o);
        }

        private static IReadOnlyDictionary<string, string> Vars(params string[] pairs)
        {
            var d = new Dictionary<string, string>();
            for (var i = 0; i < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
            return d;
        }

        /// <summary><c>#set %a1% = %a2%</c> … <c>#set %aN% = %a(N+1)%</c>, one per line.</summary>
        private static string Chain(int length, string prefix = "a") =>
            string.Join("\n", Enumerable.Range(1, length).Select(i => "#set %" + prefix + i + "% = %" + prefix + (i + 1) + "%"));

        [Fact]
        public void The_reported_shape_a_pipe_joined_runtime_list_inside_a_permutation_is_N_elements()
        {
            // first: pick = random_int(3,3) short-circuits; j = 0 at every step rotates left by one; take 3.
            Assert.Equal("b, c, d", Render("[<minsize=3;maxsize=3;sep=\", \">%L%]", First, Vars("L", "a|b|c|d")));
            Assert.Equal("a, b, c", Render("[<minsize=3;maxsize=3;sep=\", \">%L%]", Last, Vars("L", "a|b|c|d")));
        }

        [Fact]
        public void The_production_preset_a_size_range_sep_and_lastsep_over_eight_names_seeded()
        {
            const string list = "3 Oaks|Amigo|Amusnet|Apollo|Apparat|Barbara Bang|Belatra|BTG";
            const string template = "[<minsize=5;maxsize=7;sep=\", \";lastsep=\" and \">%L%]";
            var o = new RenderOptions { PostProcess = false, Context = Vars("L", list), Seed = "7" };
            var output = Engine.Render(template, o);

            var names = Regex.Split(output, ", | and ");
            Assert.InRange(names.Length, 5, 7);
            Assert.Equal(names.Length, names.Distinct().Count());
            foreach (var name in names) Assert.Contains(name, list.Split('|'));
            Assert.Matches(" and [^,]+$", output);
            Assert.Equal(output, Engine.Render(template, o));
        }

        [Fact]
        public void A_set_macro_wrapping_the_permutation_splices_the_list_at_its_reference()
        {
            Assert.Equal("\nb, c", Render("#set %TP% = [<minsize=2;maxsize=2;sep=\", \">%L%]\n%TP%", First, Vars("L", "a|b|c")));
        }

        [Fact]
        public void Enumerations_too_and_literals_around_the_variable_stay_their_own_elements()
        {
            Assert.Equal("z", Render("{%L%}", Last, Vars("L", "x|y|z")));
            Assert.Equal("x", Render("{a|%L%}", Sequence(1), Vars("L", "x|y")));
            Assert.Equal("a x y b", Render("[a|%L%|b]", Last, Vars("L", "x|y")));
        }

        [Fact]
        public void A_reference_inside_a_conditional_branch_is_direct_too()
        {
            // Plugin Stage 6a runs before expansion: the taken branch lands in the body ahead of the split.
            Assert.Equal("\nx y c", Render("#set %flag% = 1\n[{?flag?%L%|none}|c]", Last, Vars("L", "x|y")));
            Assert.Equal("none c", Render("[{?flag?%L%|none}|c]", Last, Vars("L", "x|y")));
        }

        [Fact]
        public void The_config_and_a_per_element_separator_are_text_to_the_plugin_so_a_reference_there_is_spliced()
        {
            Assert.Equal("a, b", Render("[<sep=\"%S%\">a|b]", Last, Vars("S", ", ")));
            Assert.Equal("a, b", Render("[a <%S%> | b]", Last, Vars("S", ", ")));
        }

        [Fact]
        public void What_does_not_split_a_top_level_reference_an_undefined_name_a_nested_constructs_own_list()
        {
            Assert.Equal("x|y", Render("%L%", First, Vars("L", "x|y")));
            Assert.Equal("%nope% b", Render("[%nope%|b]", Last));
            Assert.Equal("a y", Render("[a|{%L%}]", Last, Vars("L", "x|y")));
        }

        [Fact]
        public void A_value_without_structural_characters_renders_exactly_as_it_did_before_the_splice_existed()
        {
            // Same element count, same draws, same order: the re-read tree IS the parsed tree.
            Assert.Equal("y c x", Render("[%a%|%b%|c]", First, Vars("a", "x", "b", "y")));
            Assert.Equal("y", Render("{%a%|%b%}", Sequence(1), Vars("a", "x", "b", "y")));
        }

        [Fact]
        public void The_hop_budget_is_51_inside_a_bracket_exactly_as_outside_one()
        {
            // The corpus knot pin (render/circular-set-accumulates-then-stops), moved inside braces.
            Assert.Equal("\n" + new string('x', 51) + "%b%" + new string('y', 51), Render("#set %b% = x%b%y\n{%b%}", First));
            Assert.Equal("\n\n%b%", Render("#set %a% = %b%\n#set %b% = %a%\n{%a%}", First));
            // Reached through a macro re-parse, the construct has already spent one hop: the
            // fixpoint gets 50 passes — the 51st hop overall — and what is left is frozen. A flat
            // 51 would leave %a52% here.
            Assert.Equal("\n\n%a51%", Render("#set %v% = {%a1%|x}\n" + Chain(59) + "\n#set %a60% = END\n%v%", First));
        }

        [Fact]
        public void A_bomb_inside_a_construct_dies_at_the_budget_and_never_throws()
        {
            var output = Engine.Render("#set %a% = %b% %b%\n#set %b% = %a% %a%\n{%a%}", new RenderOptions { PostProcess = false });
            Assert.True(output.Length < 4 * 1024 * 1024);
            Assert.Contains('%', output);
        }

        [Fact]
        public void Deep_nesting_with_references_renders_instead_of_throwing()
        {
            var deep = string.Concat(Enumerable.Repeat("{%x%|", 5000)) + "z" + new string('}', 5000);
            var o = new RenderOptions { Context = Vars("x", "a"), Seed = "1" };
            Assert.NotNull(Engine.Render(deep, o));
        }

        [Fact]
        public void Neutralize_shields_the_brackets_of_a_value_but_not_its_pipe()
        {
            // Inside an author's construct a neutralized value still splits, in every engine.
            var o = new RenderOptions { PostProcess = false, Context = Vars("v", Engine.Neutralize("[a|b]")), Seed = "1" };
            Assert.Matches(@"^(\[a, b\]|b\], \[a)$", Engine.Render("[<sep=\", \">%v%]", o));
        }

        [Fact]
        public void The_51st_hop_reaches_the_body_as_text_a_chain_into_a_list_is_split_not_spliced_whole()
        {
            // 50 aliases and a terminal list: the plugin's 51 passes end with `{x|y}`, an enumeration.
            Assert.Equal("\n\ny", Render(Chain(50) + "\n#set %a51% = x|y\n{%a1%}", Last));
            // One deeper, the reference is what is left — frozen, not spliced a 52nd time.
            Assert.Equal("\n\n%a52%", Render(Chain(51) + "\n#set %a52% = x|y\n{%a1%}", Last));
        }

        /// <summary><c>#set %d0% = %x% %x%</c>, then each <c>%dN%</c> doubles the previous: 2^12 references to <c>%x%</c>.</summary>
        private static string Doubling() =>
            string.Join("\n", Enumerable.Range(0, 12).Select(i =>
                "#set %d" + i + "% = " + (i == 0 ? "%x% %x%" : "%d" + (i - 1) + "% %d" + (i - 1) + "%")));

        [Fact]
        public void A_reference_the_budget_cut_off_stays_literal_in_the_re_read_subtree()
        {
            // The fixpoint charges the first ~1 MiB and leaves the rest literal; handing those
            // leftovers to an uncharged plain-value shortcut spliced them for nothing — 4 MiB out
            // of a 1 MiB allowance, and the same door with 2^20 references is an OOM.
            var o = new RenderOptions { PostProcess = false, Context = Vars("x", new string('a', 1024)) };
            var output = Engine.Render(Doubling() + "\n{%d11%}", o);
            Assert.True(output.Length < 1024 * 1024 + 64 * 1024);
            Assert.Contains("%x%", output);
        }

        [Fact]
        public void Every_substitution_is_charged_so_a_plain_value_is_not_a_free_leaf_at_top_level_either()
        {
            var o = new RenderOptions { PostProcess = false, Context = Vars("x", new string('a', 1024)) };
            var output = Engine.Render(Doubling() + "\n%d11%", o);
            Assert.True(output.Length < 1024 * 1024 + 64 * 1024);
        }

        [Fact]
        public void A_51_deep_alias_chain_in_the_count_slot_reaches_its_number()
        {
            // A flat 50 passes stopped the count at %a51%, non-numeric, and erased the block.
            Assert.Equal("\n\none", Render(Chain(50) + "\n#set %a51% = 1\n{plural %a1%: one|many}", First, null, "en"));
        }

        [Fact]
        public void A_picked_form_whose_passes_ran_out_stays_frozen_instead_of_expanding_again()
        {
            // 52 deep: the plugin's 51 passes leave %a52% literal in the form; an unfrozen pick went on to END.
            Assert.Equal("\n\n%a52%", Render(Chain(51) + "\n#set %a52% = END\n{plural 1: %a1%|two}", First, null, "en"));
        }

        [Fact]
        public void The_census_counts_what_the_splice_renders()
        {
            var row = Vars("L", "a|b|c");
            // Before the splice both APIs agreed on the wrong answer: one option, and a longest
            // render of 5 — the joined value. MaxLength understating is the dangerous half.
            Assert.Equal(3, Engine.Combinations("{%L%}", row));
            Assert.Equal(1, Engine.MaxLength("{%L%}", row));
            // k=2: 3·2 ordered pairs, k=3: 3! orderings; "c, b, a" is 7.
            Assert.Equal(12, Engine.Combinations("[<minsize=2;sep=\", \">%L%]", row));
            Assert.Equal(7, Engine.MaxLength("[<minsize=2;sep=\", \">%L%]", row));
            // A #set macro is text to the census as it is to the renderer — no row needed.
            Assert.Equal(3, Engine.Combinations("#set %L% = a|b|c\n{%L%}"));
            // A conditional's branch lands in the body ahead of the split, for a row: three
            // elements, and with no size config a permutation orders all of them — 3! = 6.
            Assert.Equal(6, Engine.Combinations("[{?f?%L%|none}|c]", Vars("L", "x|y", "f", "1")));
            // Untouched constructs keep their old count exactly.
            Assert.Equal(6, Engine.Combinations("[a|b|c]"));
            Assert.Equal(2, Engine.Combinations("[%nope%|b]", Vars("x", "1")));
        }

        [Fact]
        public void The_census_agrees_with_an_exhaustive_enumeration_of_a_spliced_permutation()
        {
            const string t = "[<minsize=1;maxsize=2;sep=\", \">%L%]";
            var row = Vars("L", "x|y|z");
            var seen = new HashSet<string>();
            for (var trial = 0; trial < 20000; trial++)
            {
                var seq = new int[8];
                var s = trial;
                for (var i = 0; i < seq.Length; i++) { seq[i] = s % 3; s /= 3; }
                seen.Add(Render(t, Sequence(seq), row));
            }
            Assert.Equal(Engine.Combinations(t, row), seen.Count);
            Assert.Equal(Engine.MaxLength(t, row), seen.Max(o => o.Length));
        }

        [Fact]
        public void The_parser_keeps_the_raw_body_only_where_a_reference_is_direct()
        {
            // A #set on its own line inside the group is globally extracted; the stripped line
            // stays in the raw body as `\n\n`, and the re-read sees it.
            var ast = Parser.ParseTemplate("{\n#set %x% = A\n|%x%}");
            Assert.Equal("\n\n|%x%", Assert.IsType<EnumerationNode>(ast.Nodes.Single()).Raw);

            Assert.Null(Assert.IsType<EnumerationNode>(Parser.ParseSequence("{a|b}").Single()).Raw);
            Assert.Null(Assert.IsType<PermutationNode>(Parser.ParseSequence("[a|b]").Single()).Raw);

            Assert.Equal("<sep=\"%S%\">a|b", Assert.IsType<PermutationNode>(Parser.ParseSequence("[<sep=\"%S%\">a|b]").Single()).Raw);
            Assert.Equal("a <%S%> | b", Assert.IsType<PermutationNode>(Parser.ParseSequence("[a <%S%> | b]").Single()).Raw);
            // A nested construct's reference belongs to that construct, not to the outer one.
            var outer = Assert.IsType<PermutationNode>(Parser.ParseSequence("[a|{%L%}]").Single());
            Assert.Null(outer.Raw);
            Assert.NotNull(Assert.IsType<EnumerationNode>(outer.Options[1].Nodes.Single()).Raw);
            // A reference in a conditional's branch is direct.
            Assert.NotNull(Assert.IsType<PermutationNode>(Parser.ParseSequence("[{?f?%L%|none}|c]").Single()).Raw);
        }
    }
}
