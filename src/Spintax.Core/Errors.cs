using System;

namespace Spintax.Core
{
    /// <summary>
    /// Programmer-error throws (spec §9.3 — minimal, not a taxonomy). <c>Render</c> never throws
    /// on template content: a depth breach, a runaway recursion, an unresolvable include all
    /// degrade leniently to text. These surface only a broken host integration.
    /// </summary>
    public class SpintaxException : Exception
    {
        public SpintaxException(string message) : base(message) { }

        public SpintaxException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>The host's <c>IncludeResolver</c> itself threw.</summary>
    public sealed class IncludeResolverException : SpintaxException
    {
        public IncludeResolverException(string message, Exception inner) : base(message, inner) { }
    }
}
