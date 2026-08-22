using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// Plural buckets for the locales the corpus does NOT exercise (uk, be) alongside the ones it
    /// does, plus the edge counts worth pinning by hand: 1 / 2–4 / 5+ / 11 / 21.
    /// </summary>
    public class PluralsTests
    {
        private static readonly string[] Three = { "one", "few", "many" };
        private static readonly string[] Two = { "one", "many" };

        [Theory]
        [InlineData("ru")]
        [InlineData("uk")]
        [InlineData("be")]
        [InlineData("sr")]
        [InlineData("hr")]
        [InlineData("bs")]
        public void Slavic_three_form_buckets(string lang)
        {
            Assert.Equal("one", Plurals.PluralFor(lang, 1, Three));
            Assert.Equal("few", Plurals.PluralFor(lang, 2, Three));
            Assert.Equal("few", Plurals.PluralFor(lang, 3, Three));
            Assert.Equal("few", Plurals.PluralFor(lang, 4, Three));
            Assert.Equal("many", Plurals.PluralFor(lang, 5, Three));
            Assert.Equal("many", Plurals.PluralFor(lang, 0, Three));
            Assert.Equal("many", Plurals.PluralFor(lang, 11, Three));
            Assert.Equal("many", Plurals.PluralFor(lang, 12, Three));
            Assert.Equal("many", Plurals.PluralFor(lang, 14, Three));
            Assert.Equal("one", Plurals.PluralFor(lang, 21, Three));
            Assert.Equal("few", Plurals.PluralFor(lang, 22, Three));
            Assert.Equal("many", Plurals.PluralFor(lang, 111, Three));
            Assert.Equal("one", Plurals.PluralFor(lang, -1, Three));
            Assert.Equal(3, Plurals.PluralArity(lang));
        }

        [Theory]
        [InlineData("en")]
        [InlineData("")]
        [InlineData("de")]
        public void Two_form_default(string lang)
        {
            Assert.Equal("one", Plurals.PluralFor(lang, 1, Two));
            Assert.Equal("many", Plurals.PluralFor(lang, 0, Two));
            Assert.Equal("many", Plurals.PluralFor(lang, 2, Two));
            Assert.Equal("many", Plurals.PluralFor(lang, 21, Two));
            Assert.Equal("one", Plurals.PluralFor(lang, -1, Two));
            Assert.Equal(2, Plurals.PluralArity(lang));
        }

        [Fact]
        public void A_missing_form_is_empty_not_a_throw()
        {
            Assert.Equal("", Plurals.PluralFor("ru", 5, new[] { "one", "few" }));
            Assert.Equal("", Plurals.PluralFor("en", 2, new[] { "one" }));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("ru", "ru")]
        [InlineData("RU", "ru")]
        [InlineData("pt-BR", "pt")]
        [InlineData("uk_UA", "uk")]
        [InlineData("sr-Cyrl", "sr")]
        [InlineData("sr-Latn", "sr")]
        [InlineData("sr_RS", "sr")]
        [InlineData("srp", "srp")]
        public void Base_language_normalisation(string? locale, string expected)
        {
            Assert.Equal(expected, Plurals.NormalizeBaseLang(locale));
        }

        [Fact]
        public void The_default_arity_is_derived_from_the_table()
        {
            Assert.Equal(2, Plurals.DefaultPluralArity);
        }
    }
}
