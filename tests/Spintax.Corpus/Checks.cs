using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Spintax.Core;

namespace Spintax.Corpus
{
    /// <summary>
    /// Runs one case against the engine and judges it by the corpus README's rules — the same
    /// rules the TS and Python harnesses apply, so a green here means the same thing it means
    /// there. Returns <c>null</c> on PASS, otherwise the reason.
    /// </summary>
    public static class Checks
    {
        /// <summary>
        /// The engine id this runner answers to. The schema's enum has no token for this port
        /// yet; until it does we assert every case that names <c>ts</c> or names nobody — the
        /// contract we follow (glyph-restoring <c>neutralize</c>, TS post-process).
        /// </summary>
        public const string EngineId = "ts";

        public static bool IsForThisEngine(CorpusCase c) =>
            c.Engines is null || c.Engines.Contains(EngineId);

        public static string? Run(CorpusCase c)
        {
            switch (c.Op)
            {
                case "render":
                    return c.Kind == "rng" ? CheckRngRender(c) : CheckExact(c, Render(c));
                case "validate":
                    return CheckValidate(c);
                case "extract":
                    return CheckExtract(c);
                case "neutralize":
                    return CheckExact(c, Engine.Neutralize(c.Template));
                default:
                    return $"unknown op '{c.Op}'";
            }
        }

        // ── render ──────────────────────────────────────────────────────────────────────

        private static RenderOptions Options(CorpusCase c)
        {
            var context = c.Context is null ? null : new Dictionary<string, string>(c.Context, StringComparer.Ordinal);
            if (context != null && c.NeutralizeContext != null)
            {
                // The harness neutralizes these keys before rendering — the neutralize→render
                // round-trip, asserted on the final literal output, mechanism-independent.
                foreach (var key in c.NeutralizeContext)
                    if (context.TryGetValue(key, out var v)) context[key] = Engine.Neutralize(v);
            }
            return new RenderOptions
            {
                Context = context,
                Locale = c.Locale,
                PostProcess = c.PostProcess ?? true,
                Seed = c.Kind == "rng" ? c.SeedText : null,
            };
        }

        private static string Render(CorpusCase c)
        {
            var options = Options(c);
            // A deterministic case is fixed by its rng strategy (or `first`), never by seeding a
            // real PRNG; a kind:rng case goes through the public, seeded entry point.
            return c.Kind == "rng"
                ? Engine.Render(c.Template, options)
                : Engine.RenderWith(c.Template, RngStrategy.FromFixture(c.Rng), options);
        }

        private static string? CheckExact(CorpusCase c, string actual)
        {
            var want = c.Expect.GetProperty("output").GetString() ?? "";
            return string.Equals(want, actual, StringComparison.Ordinal)
                ? null
                : $"want={Show(want)} got={Show(actual)}";
        }

        private static string? CheckRngRender(CorpusCase c)
        {
            var first = Render(c);
            // `reproducible` is the only promise, and it is within-engine: a fresh RNG from the
            // same seed must give the same output.
            var second = Render(c);
            if (!string.Equals(first, second, StringComparison.Ordinal))
                return $"same seed gave different output: {Show(first)} then {Show(second)}";

            var e = c.Expect;
            if (e.TryGetProperty("oneOf", out var oneOf))
            {
                var set = Strings(oneOf);
                if (!set.Contains(first)) return $"got={Show(first)} not in oneOf={Show(set)}";
            }

            var hasSubset = e.TryGetProperty("subsetOf", out var subsetOf);
            var hasSize = e.TryGetProperty("sizeRange", out var sizeRange);
            if (hasSubset || hasSize)
            {
                var sep = e.TryGetProperty("separator", out var sepEl) ? sepEl.GetString() ?? " " : " ";
                // A `lastSeparator` ("a, b and c") is folded into the plain one before splitting.
                // The TS and Python harnesses ignore the field; no fixture uses it yet, so this
                // is ahead of them, not different from them.
                var joined = first;
                if (e.TryGetProperty("lastSeparator", out var lastEl) && lastEl.GetString() is string last
                    && last.Length > 0 && last != sep)
                    joined = joined.Replace(last, sep);
                // JS split("") yields one token per UTF-16 unit; .NET's Split treats "" as no-op.
                var tokens = joined.Length == 0 ? Array.Empty<string>()
                    : sep.Length == 0 ? joined.Select(ch => ch.ToString()).ToArray()
                    : joined.Split(new[] { sep }, StringSplitOptions.None);
                if (hasSubset)
                {
                    var allowed = new HashSet<string>(Strings(subsetOf), StringComparer.Ordinal);
                    var stray = tokens.Where(t => !allowed.Contains(t)).ToList();
                    if (stray.Count > 0) return $"tokens {Show(stray)} not drawn from subsetOf";
                    // A permutation draws WITHOUT replacement. Together with subsetOf and an
                    // exhaustive sizeRange this rejects a shuffle that repeats or drops an element.
                    if (tokens.Distinct(StringComparer.Ordinal).Count() != tokens.Length)
                        return $"repeated element in {Show(tokens)}";
                }
                if (hasSize)
                {
                    var lo = sizeRange[0].GetInt32();
                    var hi = sizeRange[1].GetInt32();
                    if (tokens.Length < lo || tokens.Length > hi)
                        return $"{tokens.Length} elements, expected {lo}..{hi}: {Show(first)}";
                }
            }
            return null;
        }

        // ── validate ────────────────────────────────────────────────────────────────────

        private static string? CheckValidate(CorpusCase c)
        {
            var actual = Engine.Validate(c.Template, new ValidateOptions
            {
                Locale = c.Locale,
                KnownIncludes = c.KnownIncludes,
            });
            var verdict = actual.Any(d => d.Severity == Severity.Error) ? "invalid" : "valid";
            var wantVerdict = c.Expect.GetProperty("verdict").GetString();
            var codes = Show(actual.Select(d => $"{d.Code}:{SeverityName(d.Severity)}@{d.Line}:{d.Column}").ToList());
            if (verdict != wantVerdict) return $"verdict={verdict} want={wantVerdict} diagnostics={codes}";

            // Diagnostics are a SUBSET assertion on the stated fields only: `code` is
            // parity-gated, wording is not, position only when the fixture states it.
            if (!c.Expect.TryGetProperty("diagnostics", out var wanted)) return null;
            foreach (var w in wanted.EnumerateArray())
            {
                var code = w.GetProperty("code").GetString();
                var sev = w.TryGetProperty("severity", out var s) ? s.GetString() : null;
                int? line = w.TryGetProperty("line", out var l) ? l.GetInt32() : null;
                int? col = w.TryGetProperty("column", out var k) ? k.GetInt32() : null;
                var matched = actual.Any(d =>
                    d.Code == code
                    && (sev is null || SeverityName(d.Severity) == sev)
                    && (line is null || d.Line == line)
                    && (col is null || d.Column == col));
                if (!matched) return $"no diagnostic matching {w.GetRawText()}; got {codes}";
            }
            return null;
        }

        private static string SeverityName(Severity s) => s == Severity.Error ? "error" : "warning";

        // ── extract ─────────────────────────────────────────────────────────────────────

        private static string? CheckExtract(CorpusCase c)
        {
            var actual = Engine.Extract(c.Template);
            // Order-normalized, and only the keys the fixture states: most assert a subset.
            foreach (var (key, got) in new[]
                     {
                         ("refs", actual.Refs), ("sets", actual.Sets),
                         ("defs", actual.Defs), ("includes", actual.Includes),
                     })
            {
                if (!c.Expect.TryGetProperty(key, out var wantEl)) continue;
                var want = Strings(wantEl).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var have = got.OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (!want.SequenceEqual(have, StringComparer.Ordinal))
                    return $"{key}: want={Show(want)} got={Show(have)}";
            }
            return null;
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────

        private static List<string> Strings(JsonElement arr) =>
            arr.EnumerateArray().Select(x => x.GetString() ?? "").ToList();

        /// <summary>JSON-escaped so that newlines, NBSP and friends are visible in a log.</summary>
        private static string Show(string s) => JsonSerializer.Serialize(s, ShowOptions);

        private static string Show(IEnumerable<string> s) => JsonSerializer.Serialize(s, ShowOptions);

        private static readonly JsonSerializerOptions ShowOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}
