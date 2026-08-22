using System.Linq;
using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// The reference's <c>postprocess.test.ts</c>, case for case, plus the JS-vs-.NET text
    /// semantics the port had to reproduce by hand (trim set, multi-char upper-case). The
    /// corpus covers the stage too (<c>render-postprocess.json</c>); these are the unit-level
    /// pins for the shapes the corpus does not spell out.
    /// </summary>
    public class PostProcessTests
    {
        private const string Nul = "\0";

        // The char overload of DoesNotContain: the string one is culture-sensitive, and ICU treats
        // NUL as ignorable — Assert.DoesNotContain("\0", anything) fails on .NET 5+ regardless.
        private const char NulChar = (char)0;

        private static string PP(string s) => PostProcessor.PostProcess(s);

        [Theory]
        [InlineData("hello world", "Hello world")]
        [InlineData("hello. world", "Hello. World")]
        [InlineData("wait… really", "Wait… Really")]
        [InlineData("line one\nline two", "Line one\nLine two")]
        public void Capitalisation(string input, string expected) => Assert.Equal(expected, PP(input));

        [Theory]
        [InlineData("Word  with   spaces", "Word with spaces")]
        [InlineData("Word , next", "Word, next")]
        [InlineData("Hello ! World", "Hello! World")]
        [InlineData("one,two", "One, two")]
        [InlineData("Price 3,14 eur", "Price 3,14 eur")]
        [InlineData("a.b", "A. B")]
        [InlineData("  hello  ", "Hello")]
        public void Whitespace_and_punctuation(string input, string expected) => Assert.Equal(expected, PP(input));

        [Theory]
        [InlineData("Version 2.5 released", "Version 2.5 released")]
        [InlineData("visit https://example.com now", "Visit https://example.com now")]
        [InlineData("see https://example.com.", "See https://example.com.")]
        [InlineData("mail me@example.com please", "Mail me@example.com please")]
        [InlineData("visit example.com. next", "Visit example.com. Next")]
        [InlineData("open xn--e1afmapc.xn--p1ai today", "Open xn--e1afmapc.xn--p1ai today")]
        [InlineData("email mailto:contact@example.com now", "Email mailto:contact@example.com now")]
        [InlineData("<a href=\"mailto:contact@example.com\">write us</a>", "<a href=\"mailto:contact@example.com\">Write us</a>")]
        [InlineData("see mailto:contact@example.com. next", "See mailto:contact@example.com. Next")]
        [InlineData("reach us at tel:+1-800-555-0000 today", "Reach us at tel:+1-800-555-0000 today")]
        [InlineData("Текст соц. сети тут", "Текст соц. сети тут")]
        [InlineData("call Mr. smith now", "Call Mr. smith now")]
        [InlineData("See e.g. this", "See e.g. this")]
        public void Shielding(string input, string expected) => Assert.Equal(expected, PP(input));

        [Fact]
        public void A_Cyrillic_multi_dot_abbreviation_is_NOT_shielded_in_any_engine()
        {
            // Multi-dot uses JS `\b`, which is ASCII — so т.д. is mangled, and parity holds.
            Assert.NotEqual("и т.д. далее", PP("и т.д. далее"));
        }

        [Fact]
        public void Shield_heavy_text_round_trips()
        {
            Assert.Equal(
                "Visit https://example.com/a?b=1 or mail me@example.co.uk before 3.14, e.g. now",
                PP("visit https://example.com/a?b=1 or mail me@example.co.uk before 3.14, e.g. now"));
            var many = string.Join(" ", Enumerable.Range(0, 40).Select(i => $"see https://example.com/p{i} and 1.{i} now."));
            var want = string.Join(" ", Enumerable.Range(0, 40).Select(i => $"See https://example.com/p{i} and 1.{i} now."));
            Assert.Equal(want, PP(many));
        }

        [Fact]
        public void A_literal_NUL_in_the_input_keeps_the_per_key_loop_quirks_and_all()
        {
            Assert.Equal("See https://example.com and https://example.com now",
                PP($"see {Nul}URL_0{Nul} and https://example.com now"));
            Assert.Equal($"Hello world{Nul}DOM_2http://x.io/p?q=1",
                PP($"hello world{Nul}DOM_2http://x.io/p?q=1"));
            Assert.Equal($"</p>{Nul}NUM_9{Nul}http://x.io/p?q=1tel:+1-555-0100. {Nul}tel:+1-555-0100",
                PP($"</p>{Nul}NUM_9{Nul}http://x.io/p?q=1{Nul}URI_1{Nul}. {Nul}tel:+1-555-0100"));
        }

        [Fact]
        public void Adjacent_placeholders_around_an_author_written_key_name()
        {
            const string src = "https://a.io e.g. URL_0mailto:x@y.io";
            Assert.Equal(src, PP(src));
            Assert.DoesNotContain(NulChar, PP(src));
            Assert.Equal("Hello worldт.д.URL_0http://x.io/p?q=1", PP("hello worldт.д.URL_0http://x.io/p?q=1"));
        }

        [Theory]
        [InlineData("mailto:sales@example.com?body=see%20https://shop.example.com/cart", "mailto:sales@example.com?body=see%20https://shop.example.com/cart")]
        [InlineData("write to mailto:a@b.com?subject=Re:%20https://x.io/p please", "Write to mailto:a@b.com?subject=Re:%20https://x.io/p please")]
        [InlineData("contact mailto:https://shop.example.com/cart now", "Contact mailto:https://shop.example.com/cart now")]
        [InlineData("call tel:+1-555-0100,https://x.io/p now", "Call tel:+1-555-0100,https://x.io/p now")]
        [InlineData("https://x.io/?to=mailto:a@b.com", "https://x.io/?to=mailto:a@b.com")]
        [InlineData("see https://x.io/a.mailto:contact@example.com now", "See https://x.io/a.mailto:contact@example.com now")]
        [InlineData("https://x.io/p?q=tel:+1-555-0100 now", "https://x.io/p?q=tel:+1-555-0100 now")]
        [InlineData("mailto:plain@example.com", "mailto:plain@example.com")]
        [InlineData("write mailto:contact@example.com now", "Write mailto:contact@example.com now")]
        public void Overlapping_URIs_shield_as_one_token(string input, string expected)
        {
            Assert.Equal(expected, PP(input));
            Assert.DoesNotContain(NulChar, PP(input));
        }

        [Theory]
        [InlineData("¿cómo estás?", "¿Cómo estás?")]
        [InlineData("¡genial!", "¡Genial!")]
        [InlineData("hola. ¿cómo estás? ¡genial!", "Hola. ¿Cómo estás? ¡Genial!")]
        [InlineData("Hola. ¿ qué tal ?", "Hola. ¿Qué tal?")]
        [InlineData("¡ genial !", "¡Genial!")]
        [InlineData("Hola, ¿qué tal?", "Hola, ¿qué tal?")]
        [InlineData("<p>¿cómo estás?</p><p>¡genial!</p>", "<p>¿Cómo estás?</p><p>¡Genial!</p>")]
        [InlineData("Hola.\n¿cómo estás?", "Hola.\n¿Cómo estás?")]
        [InlineData("Elige una. (a) primero", "Elige una. (a) primero")]
        [InlineData("Он сказал. \"привет\"", "Он сказал. \"привет\"")]
        [InlineData("¡¿qué haces?!", "¡¿Qué haces?!")]
        [InlineData("¿¡qué haces!?", "¿¡Qué haces!?")]
        [InlineData("hola. ¡¿qué haces?! adiós", "Hola. ¡¿Qué haces?! Adiós")]
        [InlineData("¿<strong>cómo</strong> estás?", "¿<strong>Cómo</strong> estás?")]
        [InlineData("Hola. ¿<em>qué</em> tal?", "Hola. ¿<em>Qué</em> tal?")]
        [InlineData("<p>¿<a href=\"/ayuda\">necesitas ayuda</a>?</p>", "<p>¿<a href=\"/ayuda\">Necesitas ayuda</a>?</p>")]
        public void Spanish_sentence_openers(string input, string expected) => Assert.Equal(expected, PP(input));

        [Theory]
        [InlineData("wait... what?", "Wait... What?")]
        [InlineData("wow!!!", "Wow!!!")]
        [InlineData("really?! yes.", "Really?! Yes.")]
        [InlineData("Что?! Не может быть!!", "Что?! Не может быть!!")]
        [InlineData("wait...what?", "Wait... What?")]
        [InlineData("hola.¡genial!", "Hola. ¡Genial!")]
        public void Sentence_punctuation_runs(string input, string expected) => Assert.Equal(expected, PP(input));

        // ── the JS text semantics the port reproduces by hand ─────────────────────────────

        [Fact]
        public void Trim_follows_JavaScript_not_the_BCL()
        {
            // JS trims U+FEFF and NBSP and LS; it keeps NUL and NEL (U+0085). .NET's Trim() does
            // the opposite on FEFF and NEL (measured 2026-08-21).
            Assert.Equal("X", JsText.Trim("\uFEFFX\u00A0\u2028"));
            Assert.Equal("X\u0085", JsText.Trim("X\u0085"));
            Assert.Equal("X\0", JsText.Trim("X\0 "));
            // Trim runs LAST: a leading U+FEFF is not part of the capitaliser's lead, so the
            // first letter stays lower-case and only then does the BOM go (measured in Node).
            Assert.Equal("hello", PP("\uFEFFhello\u00A0"));
        }

        [Fact]
        public void Upper_case_expands_like_JavaScript()
        {
            Assert.Equal("SS", JsText.Upper('ß'));
            Assert.Equal("FI", JsText.Upper('ﬁ'));
            Assert.Equal("ʼN", JsText.Upper('ŉ'));
            Assert.Equal("Ш", JsText.Upper('ш'));
            Assert.Equal("SSoße", PP("ßoße"));
        }

    }
}
