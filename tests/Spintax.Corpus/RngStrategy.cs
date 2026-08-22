using System;
using System.Text.Json;
using Spintax.Core;

namespace Spintax.Corpus
{
    /// <summary>
    /// A fixture's <c>rng</c> field → the engine's <see cref="Rng"/> seam. Mirrors the TS harness
    /// (<c>test/corpus-harness.ts</c>) and the Python one (<c>tests/rng_strategy.py</c>), which in
    /// turn mirror the PHP plugin's <c>make_first / make_last / make_sequence</c>:
    /// <c>"first"</c> ⇒ min, <c>"last"</c> ⇒ max, <c>{sequence:[…]}</c> ⇒ each value is a RAW
    /// return clamped to <c>[min, max]</c>, consumed in order, the last one reused after
    /// exhaustion.
    /// </summary>
    /// <remarks>
    /// The sequence strategy is the discriminator between <c>#set</c> (re-rolled at every
    /// reference) and <c>#def</c> (rolled once): under <c>first</c> both render identically.
    /// </remarks>
    internal static class RngStrategy
    {
        /// <summary>
        /// An absent <c>rng</c> defaults to <c>first</c> — the reference harness does the same.
        /// Sound only because such fixtures do not select; a real PRNG there would make every
        /// deterministic case a coin flip.
        /// </summary>
        public static Rng FromFixture(JsonElement? strategy)
        {
            if (strategy is null) return First;
            var s = strategy.Value;

            if (s.ValueKind == JsonValueKind.String)
            {
                switch (s.GetString())
                {
                    case "first": return First;
                    case "last": return Last;
                    default: throw new ArgumentException($"unknown rng strategy '{s.GetString()}'");
                }
            }

            if (s.ValueKind == JsonValueKind.Object && s.TryGetProperty("sequence", out var seqEl))
            {
                var seq = new int[seqEl.GetArrayLength()];
                var n = 0;
                foreach (var v in seqEl.EnumerateArray()) seq[n++] = v.GetInt32();
                var i = 0;
                return (min, max) =>
                {
                    var raw = seq.Length == 0 ? min : seq[Math.Min(i, seq.Length - 1)];
                    i++;
                    return Math.Max(min, Math.Min(max, raw));
                };
            }

            throw new ArgumentException($"unrecognised rng strategy: {s.GetRawText()}");
        }

        private static int First(int min, int max) => min;

        private static int Last(int min, int max) => max;
    }
}
