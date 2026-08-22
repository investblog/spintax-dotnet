using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// docs/PROPOSAL-quality-tooling.md lists traps met on a live pool. The two that are engine
    /// properties are pinned here against the reference's measured behaviour (MCP oracle,
    /// 2026-08-21).
    /// </summary>
    public class ProposalTrapsTests
    {
        private static Rng First => (min, _) => min;

        [Fact]
        public void A_sep_value_is_not_unescaped_backslash_n_is_two_characters()
        {
            // The language has no escape mechanism; the reference renders the two characters
            // (oracle: "C\\na\\nb"), and validate says nothing.
            var template = "[<sep=\"\\n\">a|b|c]";
            var output = Engine.RenderWith(template, First, new RenderOptions { PostProcess = false });
            Assert.Contains("\\n", output);
            Assert.DoesNotContain('\n', output);
            Assert.Empty(Engine.Validate(template));
        }

        [Fact]
        public void A_three_form_plural_without_a_locale_is_warned_about_not_passed_silently()
        {
            // spintax-js#65: the reference answers with plural.locale-missing; the trap as written
            // ("passes the check") predates that fix.
            var d = Assert.Single(Engine.Validate("{plural 2: a|b|c}"));
            Assert.Equal("plural.locale-missing", d.Code);
            Assert.Equal(Severity.Warning, d.Severity);
        }
    }
}
