using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Spintax.Corpus
{
    /// <summary>
    /// Finds and loads the golden corpus. It is read from a checkout of <c>spintax-js</c>, never
    /// vendored (ADR-0001): <c>SPINTAX_FIXTURES</c> first, then the sibling checkout next to this
    /// repository. Absence is reported, never papered over with an empty list.
    /// </summary>
    public static class CorpusLocator
    {
        public const string EnvVar = "SPINTAX_FIXTURES";

        /// <summary>
        /// Floors, not equalities: the corpus grows and a runner that breaks on every addition
        /// trains people to edit the number. The case floor is today's full size and applies to
        /// the cases this engine ASSERTS (skips excluded), so that once the baseline is empty a
        /// truncated checkout — or a corpus filtered away by `engines` — cannot go green.
        /// </summary>
        public const int MinFiles = 7;
        public const int MinAssertedCases = 258;

        public static string HowTo =>
            $"Point {EnvVar} at a checkout of the corpus, e.g.\n" +
            $"  {EnvVar}=W:\\projects\\spintax-js\\packages\\conformance\\fixtures\n" +
            "or clone it next to this repository:\n" +
            "  git clone https://github.com/investblog/spintax-js ../spintax-js";

        /// <summary>The fixtures directory, or <c>null</c> when none resolves.</summary>
        public static string? Find(string? explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
                return Directory.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;

            var env = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(env))
                return Directory.Exists(env) ? Path.GetFullPath(env) : null;

            var root = RepoRoot();
            if (root is null) return null;
            var sibling = Path.GetFullPath(Path.Combine(root, "..", "spintax-js", "packages", "conformance", "fixtures"));
            return Directory.Exists(sibling) ? sibling : null;
        }

        /// <summary>Walk up from the binary to the directory holding <c>Spintax.sln</c>.</summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Spintax.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>Every case, in file order then declaration order.</summary>
        public static List<CorpusCase> Load(string dir, out int fileCount)
        {
            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            fileCount = files.Length;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
            var cases = new List<CorpusCase>();
            foreach (var f in files)
            {
                var json = File.ReadAllText(f);
                var list = JsonSerializer.Deserialize<List<CorpusCase>>(json, options)
                           ?? throw new InvalidDataException($"{f}: not a JSON array of cases");
                foreach (var c in list)
                {
                    c.File = Path.GetFileName(f);
                    cases.Add(c);
                }
            }
            return cases;
        }
    }
}
