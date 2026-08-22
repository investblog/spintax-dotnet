using System;
using System.Security.Cryptography;

namespace Spintax.Core
{
    /// <summary>
    /// The RNG seam: an inclusive <c>(min, max) → int</c>, mirroring the reference's
    /// <c>Rng</c> type and the PHP plugin's injectable <c>$random_fn</c>. The engine never
    /// calls it when <c>min == max</c> — a fixture's recorded sequence counts real choices only.
    /// </summary>
    internal delegate int Rng(int min, int max);

    /// <summary>
    /// Port of <c>internal/rng.ts</c>: a seeded RNG is mulberry32 over an FNV-1a hash of the
    /// seed string, so a string seed gives the same draw sequence here as in the reference.
    /// Reproducibility is a promise within this engine only (spec §3.2); the shared sequence
    /// is a convenience for anyone comparing engines by hand, not a contract.
    /// </summary>
    internal static class Rngs
    {
        /// <summary>
        /// <c>null</c> ⇒ nondeterministic. Each call owns its generator state — nothing is
        /// shared between renders, so dozens of parallel instances cannot collide.
        /// </summary>
        public static Rng FromSeed(string? seed)
        {
            var next = Mulberry32(seed is null ? EntropySeed() : Fnv1a(seed));
            return (min, max) => min + (int)Math.Floor(next() * (max - min + 1));
        }

        /// <summary>
        /// Four bytes of OS entropy. Deliberately not <c>new Random()</c>: on .NET Framework it
        /// seeds from the tick count, and two renders started in the same millisecond — routine
        /// under ZennoPoster's parallel instances — would draw the same sequence.
        /// </summary>
        private static uint EntropySeed()
        {
            var bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        /// <summary>mulberry32 — the reference's PRNG, bit for bit (uint32 arithmetic).</summary>
        internal static Func<double> Mulberry32(uint seed)
        {
            var a = seed;
            return () =>
            {
                unchecked
                {
                    a += 0x6d2b79f5;
                    var t = (a ^ (a >> 15)) * (1 | a);
                    t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            };
        }

        /// <summary>FNV-1a over UTF-16 code units, as <c>charCodeAt</c> sees them.</summary>
        internal static uint Fnv1a(string s)
        {
            unchecked
            {
                var h = 2166136261u;
                foreach (var ch in s)
                {
                    h ^= ch;
                    h *= 16777619u;
                }
                return h;
            }
        }
    }
}
