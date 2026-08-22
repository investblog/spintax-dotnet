using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Spintax.Corpus
{
    /// <summary>
    /// Usage: <c>Spintax.Corpus [fixtures-dir] [--baseline known-failures.txt]</c>.
    /// The fixtures dir falls back to <c>SPINTAX_FIXTURES</c>, then to the sibling
    /// <c>spintax-js</c> checkout. Prints one line per failure, a per-op table, and a final
    /// <c>PASS= FAIL= SKIP=</c> line.
    /// </summary>
    /// <remarks>
    /// Exit codes: 0 green, 1 failures, 2 no usable corpus (never a green zero). With
    /// <c>--baseline</c> the runner is a <b>gate</b> in the Pascal port's sense: it exits 0 only
    /// when the set of failing ids equals the baseline — a new failure is a regression, and a
    /// case that starts passing has to be removed from the baseline rather than silently
    /// absorbed. That is what lets the port push while the corpus is still red, without the
    /// gate going quiet.
    /// </remarks>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);

            string? explicitDir = null, baselinePath = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--baseline" && i + 1 < args.Length) baselinePath = args[++i];
                else explicitDir = args[i];
            }

            var dir = CorpusLocator.Find(explicitDir);
            if (dir is null)
            {
                Console.Error.WriteLine("corpus: no fixtures directory found.");
                Console.Error.WriteLine(CorpusLocator.HowTo);
                return 2;
            }

            List<CorpusCase> cases;
            int fileCount;
            try
            {
                cases = CorpusLocator.Load(dir, out fileCount);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is System.Text.Json.JsonException || ex is InvalidDataException)
            {
                Console.Error.WriteLine($"corpus: cannot load fixtures from {dir}: {ex.Message}");
                return 2;
            }

            var asserted = cases.Count(Checks.IsForThisEngine);
            if (fileCount < CorpusLocator.MinFiles || asserted < CorpusLocator.MinAssertedCases)
            {
                // A path that resolves to an empty or truncated directory would let the runner
                // "succeed" over nothing. Count before trusting anything.
                Console.Error.WriteLine(
                    $"corpus: {fileCount} files / {cases.Count} cases ({asserted} asserted by this engine) in {dir} — " +
                    $"that is not the golden corpus (need >= {CorpusLocator.MinFiles} files and >= {CorpusLocator.MinAssertedCases} asserted cases).");
                return 2;
            }

            Console.WriteLine($"corpus: {cases.Count} cases in {fileCount} files from {dir}");

            var byOp = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // pass, fail, skip
            var failing = new SortedSet<string>(StringComparer.Ordinal);
            int pass = 0, fail = 0, skip = 0;

            foreach (var c in cases)
            {
                var tally = byOp.TryGetValue(c.Op, out var t) ? t : byOp[c.Op] = new int[3];

                if (!Checks.IsForThisEngine(c))
                {
                    skip++;
                    tally[2]++;
                    Console.WriteLine($"  SKIP [{c.File}] {c.Id}  engines={string.Join(",", c.Engines!)}");
                    continue;
                }

                string? reason;
                try
                {
                    reason = Checks.Run(c);
                }
                catch (Exception ex)
                {
                    reason = $"{ex.GetType().Name}: {ex.Message}";
                }

                if (reason is null)
                {
                    pass++;
                    tally[0]++;
                }
                else
                {
                    fail++;
                    tally[1]++;
                    failing.Add(BaselineKey(c));
                    Console.WriteLine($"  FAIL [{c.File}] {c.Id}  {reason}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{"op",-12}{"pass",6}{"fail",6}{"skip",6}");
            foreach (var kv in byOp)
                Console.WriteLine($"{kv.Key,-12}{kv.Value[0],6}{kv.Value[1],6}{kv.Value[2],6}");
            Console.WriteLine();
            Console.WriteLine($"PASS={pass}  FAIL={fail}  SKIP={skip}  (skip = case asserted by other engines only)");

            if (baselinePath is null) return fail == 0 ? 0 : 1;
            return CompareWithBaseline(baselinePath, failing);
        }

        /// <summary>The line format of the baseline file: <c>&lt;fixture file&gt;␠␠&lt;case id&gt;</c>.</summary>
        public static string BaselineKey(CorpusCase c) => $"{c.File}  {c.Id}";

        private static int CompareWithBaseline(string path, SortedSet<string> actual)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"corpus: baseline {path} not found — a missing baseline is not an empty one.");
                return 2;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"corpus: cannot read baseline {path}: {ex.Message}");
                return 2;
            }
            var expected = new SortedSet<string>(
                lines
                    .Select(l => { var h = l.IndexOf('#'); return (h >= 0 ? l.Substring(0, h) : l).TrimEnd(); })
                    .Where(l => l.Length > 0),
                StringComparer.Ordinal);

            var fixedNow = expected.Except(actual).ToList();
            var regressed = actual.Except(expected).ToList();
            if (fixedNow.Count == 0 && regressed.Count == 0)
            {
                Console.WriteLine($"baseline: ok — {actual.Count} known failures, no regressions ({path})");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("baseline: the failure set moved.");
            if (fixedNow.Count > 0)
            {
                Console.WriteLine($"  {fixedNow.Count} case(s) in the baseline now PASS — delete the line(s), and say so in the commit:");
                foreach (var k in fixedNow) Console.WriteLine($"  - {k}");
            }
            if (regressed.Count > 0)
            {
                Console.WriteLine($"  {regressed.Count} NEW failure(s) — a regression: fix it, do not add it to the baseline:");
                foreach (var k in regressed) Console.WriteLine($"  + {k}");
            }
            return 1;
        }
    }
}
