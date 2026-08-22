using System;
using System.Collections.Generic;

namespace Spintax.Core
{
    /// <summary>Options for <see cref="Engine.Render"/> — the port of <c>RenderOptions</c>.</summary>
    public sealed class RenderOptions
    {
        /// <summary>Variable map; values are author-controlled by default (spec §6).</summary>
        public IReadOnlyDictionary<string, string>? Context { get; set; }

        /// <summary>
        /// Deterministic seed; <c>null</c> ⇒ nondeterministic. The reference accepts a number
        /// or a string; here a seed is always a string (a numeric fixture seed is passed as its
        /// decimal text). Reproducibility is within this engine only — cross-engine sequence
        /// parity is a non-goal (spec §3.2).
        /// </summary>
        public string? Seed { get; set; }

        /// <summary>Plural-bucket locale (spec §3.1); <c>null</c> ⇒ the default 2-form.</summary>
        public string? Locale { get; set; }

        /// <summary>Synchronous <c>#include</c> resolver; <c>null</c> ⇒ includes disabled.</summary>
        public Func<string, string?>? IncludeResolver { get; set; }

        /// <summary>
        /// Cosmetic post-process, default <c>true</c>. <c>false</c> skips the cosmetics only —
        /// the mandatory neutralize safety-restore still runs (spec §6).
        /// </summary>
        public bool PostProcess { get; set; } = true;

        /// <summary><c>#include</c> + nesting guard; defaults to <see cref="DefaultMaxDepth"/>.</summary>
        public int MaxDepth { get; set; } = DefaultMaxDepth;

        /// <summary>
        /// Observer for <c>{plural …}</c> blocks the renderer could not resolve. Observation
        /// ONLY: the output degrades exactly as it does without it, and the host decides whether
        /// a report is fatal.
        /// </summary>
        public Action<PluralIssue>? OnPluralError { get; set; }

        /// <summary>The reference's <c>DEFAULT_MAX_DEPTH</c>.</summary>
        public const int DefaultMaxDepth = 20;
    }

    /// <summary>Options for <see cref="Engine.Validate"/> and <see cref="Engine.Analyze"/>.</summary>
    public sealed class ValidateOptions
    {
        /// <summary>Plural-bucket locale for arity verdicts; <c>null</c> ⇒ the default 2-form.</summary>
        public string? Locale { get; set; }

        /// <summary>Allow-list of include slugs; enables the <c>include.unknown-target</c> verdict.</summary>
        public IReadOnlyList<string>? KnownIncludes { get; set; }

        /// <summary>
        /// Names the host supplies at render time. Suppresses <c>variable.undefined</c> for them;
        /// the verdict is unaffected. Case-insensitive.
        /// </summary>
        public IReadOnlyList<string>? KnownVariables { get; set; }
    }
}
