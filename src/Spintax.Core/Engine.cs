using System;
using System.Collections.Generic;

namespace Spintax.Core
{
    /// <summary>
    /// The engine's public surface — a port of <c>@spintax/core</c>'s <c>index.ts</c> (spec §9.2):
    /// render / validate / extract / analyze / neutralize. Stateless and thread-safe by
    /// construction: no mutable statics, every call owns its state.
    /// </summary>
    /// <remarks>
    /// Flat signatures on purpose — strings, dictionaries, small DTOs, no <c>Task&lt;T&gt;</c> —
    /// so the engine drops into hosts that load a dll by name and compile snippets against it.
    /// Rendering never throws on content: malformed markup degrades, as in the reference engine.
    /// </remarks>
    public static class Engine
    {
        /// <summary>Render a template to one finished text.</summary>
        public static string Render(string template, RenderOptions? options = null)
        {
            return RenderWith(template, Rngs.FromSeed(options?.Seed), options);
        }

        /// <summary>
        /// Validate a template. The verdict is "invalid" when any diagnostic has
        /// <see cref="Severity.Error"/>; warnings never invalidate.
        /// </summary>
        public static IReadOnlyList<Diagnostic> Validate(string template, ValidateOptions? options = null)
        {
            return Validator.ValidateTemplate(template, options);
        }

        /// <summary>Names a template references and defines, and the includes it asks for.</summary>
        public static ExtractResult Extract(string template)
        {
            return Extractor.ExtractFromSource(template);
        }

        /// <summary>Extract + validate + a best-effort construct census, in one pass.</summary>
        public static Analysis Analyze(string template, ValidateOptions? options = null)
        {
            var ast = Parser.ParseTemplate(template);
            var extracted = Extractor.ExtractFromSource(ast.Source);
            return new Analysis(extracted, Validator.ValidateTemplate(ast.Source, options),
                CountConstructs(ast, extracted.Includes.Count));
        }

        /// <summary>
        /// Best-effort construct census (spec §9.3) — author-visible constructs, NOT a
        /// variant-cardinality promise. <c>set</c>/<c>include</c> are directive counts; the rest
        /// are tree node types, nested ones included, literals excluded.
        /// </summary>
        private static IReadOnlyDictionary<string, int> CountConstructs(ParsedAst ast, int includeCount)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["enumeration"] = 0,
                ["permutation"] = 0,
                ["variable"] = 0,
                ["conditional"] = 0,
                ["plural"] = 0,
            };
            AstWalk.Walk(ast.Nodes, n =>
            {
                switch (n)
                {
                    case EnumerationNode _: counts["enumeration"]++; break;
                    case PermutationNode _: counts["permutation"]++; break;
                    case VariableNode _: counts["variable"]++; break;
                    case ConditionalNode _: counts["conditional"]++; break;
                    case PluralNode _: counts["plural"]++; break;
                }
            });
            counts["set"] = ast.SetDefs.Count;
            counts["include"] = includeCount;
            return counts;
        }

        /// <summary>
        /// The exact number of distinct choice paths the template allows — counted by walking
        /// the tree, never sampled; it is the number of different texts when no two options
        /// spell the same thing (<c>{a|a}</c> counts 2). With <c>vars</c>: for that one row
        /// (conditionals and plurals resolve by the data); without: across all data — every
        /// branch, every plural form. Saturates at <see cref="long.MaxValue"/>.
        /// </summary>
        public static long Combinations(string template, IReadOnlyDictionary<string, string>? vars = null, string? locale = null)
        {
            return Census.Combinations(Parser.ParseTemplate(template), vars, locale);
        }

        /// <summary>The length (UTF-16 units, before post-process) of the longest render the template allows; same two readings as <see cref="Combinations"/>.</summary>
        public static long MaxLength(string template, IReadOnlyDictionary<string, string>? vars = null, string? locale = null)
        {
            return Census.MaxLength(Parser.ParseTemplate(template), vars, locale);
        }

        /// <summary>
        /// Shield a value so that its structural characters (<c>{ } [ ] | %</c> …) survive a
        /// render as literal text instead of being read as markup.
        /// </summary>
        public static string Neutralize(string value)
        {
            return Shield.Neutralize(value);
        }

        /// <summary>
        /// The full pipeline with an injected RNG — the seam shared by <see cref="Render"/>
        /// (which seeds an RNG from <see cref="RenderOptions.Seed"/>) and the corpus runner
        /// (which injects a fixture's <c>rng</c> strategy). Internal on purpose: the public
        /// contract takes a seed, never an RNG.
        /// </summary>
        internal static string RenderWith(string template, Rng rng, RenderOptions? options = null)
        {
            return RenderParsed(Parser.ParseTemplate(template), rng, options);
        }

        /// <summary>Parse once, to render the same tree many times (the facade's RenderMany / Lint).</summary>
        internal static ParsedAst Parse(string template) => Parser.ParseTemplate(template);

        /// <summary>The port of <c>internal/pipeline.ts</c> over an already parsed tree.</summary>
        internal static string RenderParsed(ParsedAst ast, Rng rng, RenderOptions? options = null)
        {
            // Order: parse (sanitises) → build vars, roll #def, walk (+ #include) → cosmetic
            // post-process (if on) → mandatory neutralize restore.
            var o = options ?? new RenderOptions();
            var ctx = new RenderCtx(
                o.Context ?? new Dictionary<string, string>(),
                rng,
                o.Locale ?? "",
                o.IncludeResolver,
                o.MaxDepth,
                Array.Empty<string>(),
                // One allowance for the whole call: includes re-enter RenderAst, and a budget
                // made there would be a budget per subtree.
                new Budget(Renderer.MaxExpansionChars),
                o.OnPluralError);
            var output = Renderer.RenderAst(ast, ctx);
            if (o.PostProcess) output = PostProcessor.PostProcess(output);
            return Shield.SafetyRestore(output);
        }

        /// <summary>The seeded RNG the public <see cref="Render"/> uses — exposed to the facade so a parsed tree can be rendered with a seed.</summary>
        internal static Rng RngFromSeed(string? seed) => Rngs.FromSeed(seed);
    }
}
