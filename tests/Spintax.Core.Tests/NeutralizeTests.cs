using Xunit;

namespace Spintax.Core.Tests
{
    public class NeutralizeTests
    {
        [Fact]
        public void A_value_with_no_structural_chars_is_the_same_instance()
        {
            const string plain = "plain text, no markup — даже с кириллицей";
            Assert.Same(plain, Engine.Neutralize(plain));
        }

        [Fact]
        public void Structural_chars_are_shielded_and_restored()
        {
            const string raw = "A {x|y} [z] 50% #h";
            var n = Engine.Neutralize(raw);
            foreach (var ch in "{}[]%#") Assert.DoesNotContain(ch, n);
            Assert.Equal(raw.Length, n.Length);
            Assert.Equal(raw, Shield.SafetyRestore(n));
        }

        [Fact]
        public void Each_structural_char_has_its_own_sentinel()
        {
            Assert.Equal("\uE000\uE001\uE002\uE003\uE004\uE005", Engine.Neutralize("{}[]%#"));
        }

        [Fact]
        public void Stray_sentinels_in_author_markup_are_stripped_not_restored()
        {
            Assert.Equal("xy", Shield.StripSentinels("x\uE001y"));
            Assert.Equal("x\uE006y", Shield.StripSentinels("x\uE006y")); // outside the reserved range
        }
    }
}
