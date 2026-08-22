using System.Linq;
using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// The parser has no corpus cases of its own — the tree is exercised through render and the
    /// directive grammar through extract — so the shapes that matter are pinned here, starting
    /// with the <c>\w</c> trap DECISION.md measured: a Cyrillic directive name is NOT a directive.
    /// </summary>
    public class ParserTests
    {
        [Fact]
        public void A_Cyrillic_directive_name_is_not_a_directive()
        {
            var d = Parser.ExtractDirectives("#set %имя% = 1\nx");
            Assert.Empty(d.SetDefs);
            Assert.Empty(d.Occurrences);
            Assert.Equal("#set %имя% = 1\nx", d.Body);
        }

        [Fact]
        public void Directives_are_line_anchored_lower_cased_and_stripped()
        {
            var d = Parser.ExtractDirectives("a\n  #set %Greeting% = Hello {x|y}\n#def %B% =\n%greeting%");
            Assert.Equal("Hello {x|y}", d.SetDefs["greeting"]);
            Assert.Equal("", d.DefDefs["b"]);
            Assert.Equal(new[] { ("set", "greeting", 2), ("def", "b", 3) },
                d.Occurrences.Select(o => (o.Kind, o.Name, o.Line)).ToArray());
            // Two stripped lines leave "\n\n\n", which the `\n{3,}` collapse turns into "\n\n".
            Assert.Equal("a\n\n%greeting%", d.Body);
        }

        [Theory]
        [InlineData("#set %a% = 1\r\nx", "1")]     // CRLF strips cleanly
        [InlineData("x\r#set %a% = 1", "1")]       // a bare CR ends a line (JS multiline ^)
        [InlineData("x\u2028#set %a% = 1\u2028y", "1")] // LS too
        public void A_directive_line_ends_at_any_JS_line_terminator(string src, string value)
        {
            var d = Parser.ExtractDirectives(src);
            Assert.Equal(value, d.SetDefs["a"]);
        }

        [Fact]
        public void Comments_are_stripped_before_anything_else()
        {
            Assert.Equal("a  b", Parser.StripComments("a /# hidden #/ b"));
            Assert.Equal("/#\n{a", Parser.StripComments("/#\n{a")); // unterminated: nothing stripped
        }

        [Fact]
        public void Enumeration_conditional_plural_variable_and_literal()
        {
            var nodes = Parser.ParseSequence("x {a|b} %v% {?flag?y|n} {?!flag?z} {plural %n%: one|few|many} {?bad}");
            Assert.Collection(nodes,
                n => Assert.Equal("x ", Assert.IsType<LiteralNode>(n).Value),
                n => Assert.Equal(2, Assert.IsType<EnumerationNode>(n).Options.Count),
                n => Assert.Equal(" ", Assert.IsType<LiteralNode>(n).Value),
                n => Assert.Equal("v", Assert.IsType<VariableNode>(n).Name),
                n => Assert.Equal(" ", Assert.IsType<LiteralNode>(n).Value),
                n =>
                {
                    var c = Assert.IsType<ConditionalNode>(n);
                    Assert.Equal("flag", c.Name);
                    Assert.False(c.Inverted);
                    Assert.Equal("y", Assert.IsType<LiteralNode>(c.Then.Single()).Value);
                    Assert.Equal("n", Assert.IsType<LiteralNode>(c.Else.Single()).Value);
                },
                n => Assert.Equal(" ", Assert.IsType<LiteralNode>(n).Value),
                n =>
                {
                    var c = Assert.IsType<ConditionalNode>(n);
                    Assert.True(c.Inverted);
                    Assert.Empty(c.Else);
                },
                n => Assert.Equal(" ", Assert.IsType<LiteralNode>(n).Value),
                n =>
                {
                    var p = Assert.IsType<PluralNode>(n);
                    Assert.Equal("%n%", p.CountRaw);
                    Assert.Equal(" one|few|many", p.FormsRaw);
                },
                n => Assert.Equal(" ", Assert.IsType<LiteralNode>(n).Value),
                n => Assert.Single(Assert.IsType<EnumerationNode>(n).Options)); // malformed conditional ⇒ enumeration
        }

        [Fact]
        public void An_unmatched_bracket_is_literal_and_a_bare_percent_too()
        {
            var nodes = Parser.ParseSequence("50% off {a|b");
            Assert.Single(nodes);
            Assert.Equal("50% off {a|b", Assert.IsType<LiteralNode>(nodes[0]).Value);
        }

        [Fact]
        public void Permutation_config_and_per_element_separators()
        {
            // A per-element separator is a trailing `<sep>` on the PREVIOUS part: `a <;>| b`.
            var p = Assert.IsType<PermutationNode>(Parser.ParseSequence("[<minsize=1;maxsize=2;sep=\", \";lastsep=\" and \"> a <;>| b | c ]").Single());
            Assert.Equal(1, p.Config.MinSize);
            Assert.Equal(2, p.Config.MaxSize);
            Assert.Equal(", ", p.Config.Sep);
            Assert.Equal(" and ", p.Config.LastSep);
            Assert.Equal(3, p.Options.Count);
            Assert.Null(p.Options[0].Separator);
            Assert.Equal(";", p.Options[1].Separator);
            Assert.Null(p.Options[2].Separator);
            Assert.Equal("a", Assert.IsType<LiteralNode>(p.Options[0].Nodes.Single()).Value);

            var single = Assert.IsType<PermutationNode>(Parser.ParseSequence("[< - >a|b]").Single());
            Assert.Equal(" - ", single.Config.Sep);
            Assert.Equal(" - ", single.Config.LastSep);
            Assert.Null(single.Config.MinSize);
        }

        [Fact]
        public void A_leading_HTML_tag_is_not_a_config()
        {
            var p = Assert.IsType<PermutationNode>(Parser.ParseSequence("[<li>a</li>|<li>b</li>]").Single());
            Assert.Equal(" ", p.Config.Sep);
            Assert.Equal(2, p.Options.Count);
            Assert.Equal("<li>a</li>", string.Concat(p.Options[0].Nodes.Cast<LiteralNode>().Select(l => l.Value)));
        }

        [Fact]
        public void Split_top_level_tracks_the_two_bracket_kinds_independently()
        {
            Assert.Equal(new[] { "a]|b" }, Parser.SplitTopLevel("a]|b"));
            Assert.Equal(new[] { "{x|y}", "z" }, Parser.SplitTopLevel("{x|y}|z"));
            Assert.Equal(new[] { "", "" }, Parser.SplitTopLevel("|"));
        }

        [Fact]
        public void Deep_nesting_does_not_overflow_the_stack()
        {
            var depth = 5000;
            var src = string.Concat(Enumerable.Repeat("{a|", depth)) + "b" + new string('}', depth);
            var nodes = Parser.ParseSequence(src);
            Assert.IsType<EnumerationNode>(nodes.Single());
        }

        [Fact]
        public void Stray_sentinels_are_stripped_from_author_markup_but_source_is_kept()
        {
            var ast = Parser.ParseTemplate("x\uE001y");
            Assert.Equal("x\uE001y", ast.Source);
            Assert.Equal("xy", Assert.IsType<LiteralNode>(ast.Nodes.Single()).Value);
        }
    }
}
