using System.Linq;
using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// The seeded generator is pinned to the reference's numbers, produced by running its
    /// <c>rng.ts</c> (mulberry32 over FNV-1a of the seed string) in Node on 2026-08-21 — so a
    /// string seed draws the same sequence here as in <c>@spintax/core</c>.
    /// </summary>
    public class RngTests
    {
        [Theory]
        [InlineData("abc", 440920331u)]
        [InlineData("sku-1:site-a", 1654581343u)]
        [InlineData("7", 839689206u)]
        [InlineData("Привет", 939894811u)]
        public void Fnv1a_matches_the_reference(string seed, uint expected)
        {
            Assert.Equal(expected, Rngs.Fnv1a(seed));
        }

        [Theory]
        [InlineData("abc", "516641,659622,1879,899349,720534", "4,4,1,6,5,3,6,4,5,3")]
        [InlineData("sku-1:site-a", "252785,65022,596164,432526,558327", "2,1,4,3,4,5,3,5,4,6")]
        [InlineData("7", "591692,704973,516414,445254,78481", "4,5,4,3,1,1,1,3,1,3")]
        [InlineData("Привет", "785624,768933,990188,330600,745218", "5,5,6,2,5,3,1,5,4,2")]
        public void Seeded_draws_match_the_reference(string seed, string wide, string dice)
        {
            var r = Rngs.FromSeed(seed);
            Assert.Equal(wide, string.Join(",", Enumerable.Range(0, 5).Select(_ => r(0, 999999))));
            var r2 = Rngs.FromSeed(seed);
            Assert.Equal(dice, string.Join(",", Enumerable.Range(0, 10).Select(_ => r2(1, 6))));
        }

        [Theory]
        [InlineData("range")]
        [InlineData(null)] // the unseeded path: OS entropy in, the same arithmetic after
        public void Draws_stay_inside_the_inclusive_range(string? seed)
        {
            // Two unseeded generators sharing a sequence is a 1-in-2^32 event, so "they differ"
            // is not a deterministic assertion and is not made; the range and the seeded pins are.
            var r = Rngs.FromSeed(seed);
            var seen = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < 10000; i++)
            {
                var v = r(3, 5);
                Assert.InRange(v, 3, 5);
                seen.Add(v);
            }
            Assert.Equal(3, seen.Count); // every value of a 3-wide range shows up in 10k draws
        }
    }
}
