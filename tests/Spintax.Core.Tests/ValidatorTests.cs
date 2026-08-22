using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// Positions. No corpus case pins line/column, and the M3 repair loop is built on them, so
    /// every expectation here is a MEASUREMENT from the reference engine through the
    /// <c>@spintax/mcp</c> oracle (2026-08-21): 1-based, columns in code points for the bracket
    /// scan, end columns exclusive.
    /// </summary>
    public class ValidatorTests
    {
        private static (string code, Severity sev, int line, int col, int? endLine, int? endCol)[] Shape(IEnumerable<Diagnostic> ds) =>
            ds.Select(d => (d.Code, d.Severity, d.Line, d.Column, d.EndLine, d.EndColumn)).ToArray();

        [Fact]
        public void Malformed_set_on_line_two_and_an_undefined_ref_on_line_one()
        {
            var ds = Engine.Validate("{a|b} %x%\n#set %имя% = 1");
            Assert.Equal(new[]
            {
                ("set.malformed", Severity.Error, 2, 1, (int?)2, (int?)15),
                ("variable.undefined", Severity.Warning, 1, 7, 1, 10),
            }, Shape(ds));
        }

        [Fact]
        public void Permutation_config_and_an_unclosed_brace()
        {
            var ds = Engine.Validate("[<minsize=x;foo=1>a|b] {a|b");
            Assert.Equal(new[]
            {
                ("bracket.unclosed", Severity.Error, 1, 24, (int?)1, (int?)25),
                ("permutation.unknown-key", Severity.Error, 1, 13, 1, 16),
                ("permutation.minsize-not-integer", Severity.Error, 1, 3, 1, 12),
            }, Shape(ds));
            Assert.Equal("foo", ds[1].Data!["key"]);
            Assert.Equal("x", ds[2].Data!["value"]);
        }

        [Fact]
        public void Cycles_self_reference_stray_closer_and_a_conditional_ref()
        {
            var ds = Engine.Validate("#set %a% = %b%\n#set %b% = %a%\n#set %c% = %c%\n%a% {?flag?y|n} a}");
            Assert.Equal(new[]
            {
                ("bracket.unexpected-closing", Severity.Error, 4, 18, (int?)4, (int?)19),
                ("variable.self-reference", Severity.Error, 3, 6, 3, 9),
                ("variable.circular-reference", Severity.Error, 1, 6, 1, 9),
                ("variable.circular-reference", Severity.Error, 2, 6, 2, 9),
                ("variable.undefined", Severity.Warning, 4, 7, 4, 11),
            }, Shape(ds));
            Assert.Equal("Circular variable reference: a → b → a.", ds[2].Message);
            Assert.Equal("Circular variable reference: b → a → b.", ds[3].Message);
        }

        [Fact]
        public void An_astral_character_is_one_column_duplicates_and_an_include_in_a_def()
        {
            var ds = Engine.Validate("😀 {a\n#set %x% = 1\n#def %x% = 2\n#def %y% = #include \"z\"\n{plural 2: one|few|many}");
            Assert.Equal(new[]
            {
                ("bracket.unclosed", Severity.Error, 1, 3, (int?)1, (int?)4),
                ("definition.duplicate-name", Severity.Error, 3, 1, null, null),
                ("def.include-in-value", Severity.Error, 4, 1, null, null),
                ("plural.locale-missing", Severity.Warning, 5, 1, 5, 25),
            }, Shape(ds));
            Assert.Equal(3, ds[3].Data!["got"]);
            Assert.Equal(2, ds[3].Data!["defaultArity"]);
        }

        [Fact]
        public void Plural_arity_is_an_error_not_a_warning_and_carries_expected_got()
        {
            var ds = Engine.Validate("{plural 2: one|few|many} {plural 1: a|b}", new ValidateOptions { Locale = "en" });
            var d = Assert.Single(ds);
            Assert.Equal(("plural.arity", Severity.Error, 1, 1, (int?)1, (int?)25), Shape(ds)[0]);
            Assert.Equal(2, d.Data!["expected"]);
            Assert.Equal(3, d.Data!["got"]);

            var ru = Engine.Validate("{plural 1: a|b}", new ValidateOptions { Locale = "ru" });
            Assert.Equal("plural.arity", Assert.Single(ru).Code);
        }

        [Fact]
        public void Known_variables_and_known_includes()
        {
            var quiet = Engine.Validate("%host% {?host?a|b}", new ValidateOptions { KnownVariables = new[] { "HOST" } });
            Assert.Empty(quiet);

            var inc = Engine.Validate("#include \"x\"", new ValidateOptions { KnownIncludes = new[] { "y" } });
            var d = Assert.Single(inc);
            Assert.Equal(("include.unknown-target", Severity.Error, 1, 11, (int?)1, (int?)12), Shape(inc)[0]);
            Assert.Equal("x", d.Data!["target"]);
        }

        [Fact]
        public void Analyze_counts_constructs_and_bundles_extract_and_validate()
        {
            var a = Engine.Analyze("#set %s% = {x|y}\n%s% %s% {a|[b|c]} {?f?t} {plural 1: a|b}\n#include \"h\"");
            Assert.Equal(new[] { "s", "f" }, a.Refs);
            Assert.Equal(new[] { "s" }, a.Sets);
            Assert.Equal(new[] { "h" }, a.Includes);
            Assert.Equal(1, a.Constructs["enumeration"]);
            Assert.Equal(1, a.Constructs["permutation"]);
            Assert.Equal(2, a.Constructs["variable"]);
            Assert.Equal(1, a.Constructs["conditional"]);
            Assert.Equal(1, a.Constructs["plural"]);
            Assert.Equal(1, a.Constructs["set"]);
            Assert.Equal(1, a.Constructs["include"]);
            Assert.False(a.Constructs.ContainsKey("def"));
            Assert.Contains(a.Diagnostics, d => d.Code == "variable.undefined" && (string)d.Data!["name"]! == "f");
        }
    }
}
