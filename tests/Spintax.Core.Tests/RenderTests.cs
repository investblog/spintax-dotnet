using System;
using System.Collections.Generic;
using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// What the corpus cannot express about render: <c>#include</c> (no resolver in the fixture
    /// schema), the plural observer, the include-resolver error, and the seam between the
    /// public seeded entry and the injected one.
    /// </summary>
    public class RenderTests
    {
        private static Rng First => (min, _) => min;

        [Fact]
        public void Include_renders_the_child_in_its_own_scope()
        {
            var o = new RenderOptions
            {
                PostProcess = false,
                Context = new Dictionary<string, string> { ["who"] = "world" },
                IncludeResolver = r => r == "hero" ? "hi %who% %local%" : null,
            };
            // The child inherits the runtime context but NOT the parent's #set. The stripped
            // directive line leaves its newline behind, as in the reference.
            Assert.Equal("\nhi world %local%\nx", Engine.RenderWith("#set %local% = L\n#include \"hero\"\nx", First, o));
        }

        [Fact]
        public void Unresolved_circular_and_too_deep_includes_resolve_to_nothing()
        {
            var o = new RenderOptions { PostProcess = false, IncludeResolver = r => r == "self" ? "#include \"self\"" : null };
            Assert.Equal("a\n\nb", Engine.RenderWith("a\n#include \"missing\"\nb", First, o)); // the line goes, its newline stays
            Assert.Equal("", Engine.RenderWith("#include \"self\"", First, o));

            var chain = new RenderOptions { PostProcess = false, MaxDepth = 3, IncludeResolver = r => "#include \"" + r + "x\"\nd" };
            // Each level adds a "d"; the chain is cut at MaxDepth, leniently. The innermost
            // include line resolves to "" and leaves its newline, hence the leading "\n".
            Assert.Equal("\nd\nd\nd", Engine.RenderWith("#include \"a\"", First, chain));
        }

        [Fact]
        public void A_throwing_resolver_is_a_programmer_error()
        {
            var o = new RenderOptions { IncludeResolver = _ => throw new InvalidOperationException("boom") };
            var ex = Assert.Throws<IncludeResolverException>(() => Engine.RenderWith("#include \"x\"", First, o));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public void Without_a_resolver_an_include_line_stays_literal()
        {
            Assert.Equal("#include \"x\"", Engine.RenderWith("#include \"x\"", First, new RenderOptions { PostProcess = false }));
        }

        [Fact]
        public void The_plural_observer_sees_the_three_codes_and_never_changes_output()
        {
            var issues = new List<PluralIssue>();
            var o = new RenderOptions { PostProcess = false, Locale = "ru", OnPluralError = issues.Add };

            // Every brace of the construct goes fullwidth, the nested ones included (global replace).
            Assert.Equal("｛plural 1: ｛a|b｝|c|d｝", Engine.RenderWith("{plural 1: {a|b}|c|d}", First, o));
            Assert.Equal("", Engine.RenderWith("{plural x: a|b|c}", First, o));
            Assert.Equal("｛plural 1: a|b｝", Engine.RenderWith("{plural 1: a|b}", First, o));
            Assert.Equal("one", Engine.RenderWith("{plural 21: one|few|many}", First, o));

            Assert.Equal(new[] { "plural.nested-brackets", "plural.count", "plural.arity" }, issues.ConvertAll(i => i.Code));
            Assert.Equal(3, issues[2].Expected);
            Assert.Equal(2, issues[2].Got);
            Assert.Equal("ru", issues[2].Locale);
        }

        [Fact]
        public void A_seeded_render_is_reproducible_and_the_default_post_process_capitalises()
        {
            var a = Engine.Render("{a|b|c} {x|y}", new RenderOptions { Seed = "demo" });
            var b = Engine.Render("{a|b|c} {x|y}", new RenderOptions { Seed = "demo" });
            Assert.Equal(a, b);
            Assert.True(char.IsUpper(a[0]), a);
            Assert.Equal("hello world", Engine.Render("hello world", new RenderOptions { PostProcess = false }));
        }

        [Fact]
        public void An_expansion_bomb_terminates_and_leaves_literal_references()
        {
            var output = Engine.RenderWith("#set %a% = %b% %b%\n#set %b% = %a% %a%\n%a%", First, new RenderOptions { PostProcess = false });
            // A fixed bound, independent of the production constant: the allowance is one
            // megabyte of substituted text, and the last substitution may overshoot by one value.
            Assert.True(output.Length <= 2 * 1024 * 1024 + 64, output.Length.ToString());
            Assert.Contains("%a%", output);
        }

        [Fact]
        public void Numeric_definition_names_roll_in_JavaScript_key_order()
        {
            // Object.keys puts array-index names first, ascending: %1% rolls before %2% although
            // it is defined second, and the RNG sequence is consumed in that order.
            var seq = new[] { 0, 1 };
            var i = 0;
            Rng rng = (min, max) => Math.Max(min, Math.Min(max, seq[Math.Min(i++, seq.Length - 1)]));
            Assert.Equal("\n\nC B", Engine.RenderWith("#def %2% = {A|B}\n#def %1% = {C|D}\n%1% %2%", rng, new RenderOptions { PostProcess = false }));
            Assert.Equal(new[] { "0", "7", "42", "b", "a", "01", "4294967295" },
                Renderer.JsKeyOrder(new[] { "b", "42", "a", "01", "7", "4294967295", "0" }));
        }

        [Theory]
        [InlineData("9223372036854779904", "ru", "{plural %n%: one|few|many}", "few")] // past long.MaxValue, exact as a double, remainder 4
        [InlineData("9007199254740993", "en", "{plural %n%: one|many}", "many")]       // 2^53 + 1 rounds to 2^53: not one
        [InlineData("-21", "ru", "{plural %n%: one|few|many}", "one")]
        public void Huge_plural_counts_follow_JavaScript_number_semantics(string count, string locale, string template, string expected)
        {
            var vars = new Dictionary<string, string> { ["n"] = count };
            Assert.Equal(expected, Engine.RenderWith(template, First,
                new RenderOptions { PostProcess = false, Locale = locale, Context = vars }));
        }

        [Fact]
        public void A_count_past_the_double_range_never_throws_and_picks_many()
        {
            var huge = new string('9', 400);
            var vars = new Dictionary<string, string> { ["n"] = huge };
            Assert.Equal("many", Engine.RenderWith("{plural %n%: one|few|many}", First,
                new RenderOptions { PostProcess = false, Locale = "ru", Context = vars }));
            Assert.Equal("", Plurals.PluralFor("ru", double.PositiveInfinity, new[] { "one", "few" }));
        }
    }
}
