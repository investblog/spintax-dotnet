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

        [Theory]
        [InlineData(0, "zero")]
        [InlineData(1, "one")]
        [InlineData(2, "two")]
        [InlineData(3, "few")]
        [InlineData(10, "few")]
        [InlineData(11, "many")]
        [InlineData(99, "many")]
        [InlineData(100, "other")]
        [InlineData(102, "other")]
        [InlineData(103, "few")]
        [InlineData(111, "many")]
        [InlineData(1000, "other")]
        [InlineData(-2, "two")]
        public void Arabic_six_forms_in_CLDR_order(double n, string expected)
        {
            var six = new[] { "zero", "one", "two", "few", "many", "other" };
            Assert.Equal(expected, Plurals.PluralFor("ar", n, six));
            Assert.Equal(6, Plurals.PluralArity("ar"));
        }

        [Fact]
        public void An_Arabic_count_past_the_double_range_is_other()
        {
            // NaN remainders compare false everywhere: JS falls through to forms[5].
            var six = new[] { "zero", "one", "two", "few", "many", "other" };
            Assert.Equal("other", Plurals.PluralFor("ar", double.PositiveInfinity, six));
            Assert.Equal("other", Plurals.PluralFor("ar", double.NaN, six));
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
