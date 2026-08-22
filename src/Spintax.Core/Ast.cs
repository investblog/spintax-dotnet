using System;
using System.Collections.Generic;

namespace Spintax.Core
{
    /// <summary>
    /// Port of <c>internal/ast.ts</c>. Tree nodes: literal / variable / enumeration /
    /// permutation / conditional / plural. Directives are NOT nodes: <c>#set</c> and <c>#def</c>
    /// are extracted globally before the tree is built (line-anchored) into
    /// <see cref="ParsedAst.SetDefs"/> / <see cref="ParsedAst.DefDefs"/>; <c>#include</c> is
    /// resolved by the renderer as a post-tree string pass and stays literal here.
    /// </summary>
    internal abstract class Node
    {
    }

    /// <summary>Verbatim text.</summary>
    internal sealed class LiteralNode : Node
    {
        public LiteralNode(string value) { Value = value; }

        public string Value { get; }
    }

    /// <summary><c>%name%</c> — name stored verbatim; lookup is case-insensitive at render.</summary>
    internal sealed class VariableNode : Node
    {
        public VariableNode(string name) { Name = name; }

        public string Name { get; }
    }

    /// <summary><c>{a|b|c}</c> — pick one option; each option is a node sequence.</summary>
    internal sealed class EnumerationNode : Node
    {
        public EnumerationNode(IReadOnlyList<IReadOnlyList<Node>> options) { Options = options; }

        public IReadOnlyList<IReadOnlyList<Node>> Options { get; }
    }

    /// <summary>Permutation <c>&lt;config&gt;</c>; a <c>null</c> size ⇒ default rules at render (spec §4.2).</summary>
    internal sealed class PermConfig
    {
        public PermConfig(int? minSize, int? maxSize, string sep, string? lastSep)
        {
            MinSize = minSize;
            MaxSize = maxSize;
            Sep = sep;
            LastSep = lastSep;
        }

        public int? MinSize { get; }

        public int? MaxSize { get; }

        public string Sep { get; }

        public string? LastSep { get; }
    }

    /// <summary>One permutation element plus its per-element separator (a trailing <c>&lt;sep&gt;</c> of the PREVIOUS part).</summary>
    internal sealed class PermOption
    {
        public PermOption(IReadOnlyList<Node> nodes, string? separator)
        {
            Nodes = nodes;
            Separator = separator;
        }

        public IReadOnlyList<Node> Nodes { get; }

        public string? Separator { get; }
    }

    /// <summary><c>[&lt;config&gt;a|b|c]</c> — select / shuffle / join.</summary>
    internal sealed class PermutationNode : Node
    {
        public PermutationNode(PermConfig config, IReadOnlyList<PermOption> options)
        {
            Config = config;
            Options = options;
        }

        public PermConfig Config { get; }

        public IReadOnlyList<PermOption> Options { get; }
    }

    /// <summary>
    /// <c>{?VAR?then|else}</c> / <c>{?!VAR?then}</c>. A malformed <c>{?…}</c> is not a
    /// conditional — the parser falls back to an enumeration, as the plugin does.
    /// </summary>
    internal sealed class ConditionalNode : Node
    {
        public ConditionalNode(string name, bool inverted, IReadOnlyList<Node> then, IReadOnlyList<Node> @else)
        {
            Name = name;
            Inverted = inverted;
            Then = then;
            Else = @else;
        }

        public string Name { get; }

        public bool Inverted { get; }

        public IReadOnlyList<Node> Then { get; }

        public IReadOnlyList<Node> Else { get; }
    }

    /// <summary>
    /// <c>{plural &lt;count&gt;: one|few|many}</c>. Both slots are kept RAW (they may hold
    /// <c>%var%</c>); the renderer expands variables in them first, then splits and picks.
    /// </summary>
    internal sealed class PluralNode : Node
    {
        public PluralNode(string countRaw, string formsRaw)
        {
            CountRaw = countRaw;
            FormsRaw = formsRaw;
        }

        public string CountRaw { get; }

        public string FormsRaw { get; }
    }

    /// <summary>
    /// The parsed tree plus the original source (raw-text checks, diagnostics) and the globally
    /// extracted directive definitions (name → raw value, name lower-cased). <c>#set</c> is a
    /// macro substituted at every reference; <c>#def</c> is rendered once and frozen.
    /// </summary>
    internal sealed class ParsedAst
    {
        public ParsedAst(string source, IReadOnlyDictionary<string, string> setDefs,
            IReadOnlyDictionary<string, string> defDefs, IReadOnlyList<Node> nodes)
        {
            Source = source;
            SetDefs = setDefs;
            DefDefs = defDefs;
            Nodes = nodes;
        }

        public string Source { get; }

        public IReadOnlyDictionary<string, string> SetDefs { get; }

        public IReadOnlyDictionary<string, string> DefDefs { get; }

        public IReadOnlyList<Node> Nodes { get; }
    }

    internal static class AstWalk
    {
        /// <summary>
        /// Pre-order depth-first walk, children in source order. Iterative on purpose (#68): a
        /// frame per nesting level overflowed on a template the parser accepts. The order is
        /// observable — <c>constructs</c> counts and <c>refs</c> order come out of it.
        /// </summary>
        public static void Walk(IReadOnlyList<Node> nodes, Action<Node> visit)
        {
            var stack = new Stack<(IReadOnlyList<Node> list, int i)>();
            stack.Push((nodes, 0));

            while (stack.Count > 0)
            {
                var (list, i) = stack.Pop();
                if (i >= list.Count) continue;
                stack.Push((list, i + 1));

                var n = list[i];
                visit(n);

                switch (n)
                {
                    case EnumerationNode e:
                        for (var k = e.Options.Count - 1; k >= 0; k--) stack.Push((e.Options[k], 0));
                        break;
                    case ConditionalNode c:
                        stack.Push((c.Else, 0));
                        stack.Push((c.Then, 0));
                        break;
                    case PermutationNode p:
                        for (var k = p.Options.Count - 1; k >= 0; k--) stack.Push((p.Options[k].Nodes, 0));
                        break;
                }
            }
        }
    }
}
