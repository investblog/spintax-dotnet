using System.Collections.Generic;

namespace Spintax.Core
{
    public enum Severity
    {
        Error,
        Warning,
    }

    /// <summary>
    /// One validation finding. <see cref="Code"/> and <see cref="Severity"/> are parity-gated by
    /// the golden corpus; wording and position are not — but <see cref="Line"/> and
    /// <see cref="Column"/> are <b>1-based</b>, and the repair loop of M3 depends on that.
    /// </summary>
    public sealed class Diagnostic
    {
        public Diagnostic(Severity severity, string code, string message, int line, int column,
            int? endLine = null, int? endColumn = null, IReadOnlyDictionary<string, object?>? data = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            Line = line;
            Column = column;
            EndLine = endLine;
            EndColumn = endColumn;
            Data = data;
        }

        /// <summary>Structured specifics keyed off <see cref="Code"/>, e.g. <c>expected: 3, got: 2</c>; values are strings or ints.</summary>
        public IReadOnlyDictionary<string, object?>? Data { get; }

        public Severity Severity { get; }

        /// <summary>Stable machine code, e.g. <c>set.malformed</c>.</summary>
        public string Code { get; }

        public string Message { get; }

        /// <summary>1-based.</summary>
        public int Line { get; }

        /// <summary>1-based.</summary>
        public int Column { get; }

        public int? EndLine { get; }

        public int? EndColumn { get; }
    }

    /// <summary>What a template references and defines — the port of <c>ExtractResult</c>.</summary>
    public class ExtractResult
    {
        public ExtractResult(IReadOnlyList<string> refs, IReadOnlyList<string> sets,
            IReadOnlyList<string> defs, IReadOnlyList<string> includes)
        {
            Refs = refs;
            Sets = sets;
            Defs = defs;
            Includes = includes;
        }

        /// <summary><c>%name%</c> references.</summary>
        public IReadOnlyList<string> Refs { get; }

        /// <summary>Names defined by <c>#set</c> — macros, re-picked at every reference.</summary>
        public IReadOnlyList<string> Sets { get; }

        /// <summary>Names defined by <c>#def</c> — rolled once per render and held.</summary>
        public IReadOnlyList<string> Defs { get; }

        public IReadOnlyList<string> Includes { get; }
    }

    /// <summary>Extraction + diagnostics + a best-effort construct census (spec §9.3).</summary>
    public sealed class Analysis : ExtractResult
    {
        public Analysis(ExtractResult extracted, IReadOnlyList<Diagnostic> diagnostics,
            IReadOnlyDictionary<string, int> constructs)
            : base(extracted.Refs, extracted.Sets, extracted.Defs, extracted.Includes)
        {
            Diagnostics = diagnostics;
            Constructs = constructs;
        }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>Counts of author-visible constructs — NOT a variant-cardinality promise.</summary>
        public IReadOnlyDictionary<string, int> Constructs { get; }
    }
}
