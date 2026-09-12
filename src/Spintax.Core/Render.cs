using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Spintax.Core
{
    /// <summary>
    /// A <c>{plural …}</c> block the renderer could not resolve, reported through
    /// <see cref="RenderOptions.OnPluralError"/>. Observation only — the output degrades exactly
    /// as it does without an observer (spec §0.1, lenient); the host decides whether it is fatal.
    /// Worth wiring when a render is PERSISTED: an unresolved count erases its block, which is
    /// indistinguishable in the output from copy that was meant to be empty.
    /// </summary>
    public sealed class PluralIssue
    {
        public PluralIssue(string code, string message, string construct, string locale, int? expected, int? got)
        {
            Code = code;
            Message = message;
            Construct = construct;
            Locale = locale;
            Expected = expected;
            Got = got;
        }

        /// <summary>
        /// <c>plural.nested-brackets</c> and <c>plural.arity</c> are the validator's codes;
        /// <c>plural.count</c> has no static counterpart on purpose — an unresolved count is a
        /// runtime-value fact.
        /// </summary>
        public string Code { get; }

        public string Message { get; }

        /// <summary>The construct as the renderer saw it, AFTER variable expansion.</summary>
        public string Construct { get; }

        /// <summary>Normalised base language the arity was judged against.</summary>
        public string Locale { get; }

        /// <summary>Arity verdicts only.</summary>
        public int? Expected { get; }

        public int? Got { get; }
    }

    /// <summary>Expansion allowance for one whole render call, includes and all (spintax-js#69). Shared by reference on purpose.</summary>
    internal sealed class Budget
    {
        public Budget(int left) { Left = left; }

        public int Left;
    }

    /// <summary>Document-level render context; threads through nested <c>#include</c> resolution.</summary>
    internal sealed class RenderCtx
    {
        public RenderCtx(IReadOnlyDictionary<string, string> runtimeContext, Rng rng, string locale,
            Func<string, string?>? resolver, int maxDepth, IReadOnlyList<string> includeStack, Budget budget,
            Action<PluralIssue>? onPluralError)
        {
            RuntimeContext = runtimeContext;
            Rng = rng;
            Locale = locale;
            Resolver = resolver;
            MaxDepth = maxDepth;
            IncludeStack = includeStack;
            Budget = budget;
            OnPluralError = onPluralError;
        }

        /// <summary>Host variable map — inherited by child includes (NOT the parent's <c>#set</c>).</summary>
        public IReadOnlyDictionary<string, string> RuntimeContext { get; }

        public Rng Rng { get; }

        public string Locale { get; }

        public Func<string, string?>? Resolver { get; }

        public int MaxDepth { get; }

        /// <summary><c>#include</c> ref chain for circular-reference detection.</summary>
        public IReadOnlyList<string> IncludeStack { get; }

        public Budget Budget { get; }

        public Action<PluralIssue>? OnPluralError { get; }

        public RenderCtx ForInclude(string reference)
        {
            var stack = new List<string>(IncludeStack) { reference };
            return new RenderCtx(RuntimeContext, Rng, Locale, Resolver, MaxDepth, stack, Budget, OnPluralError);
        }
    }

    /// <summary>Options of one tree walk. <see cref="Vars"/> is the merged map, keys lower-cased (runtime context wins over <c>#set</c>).</summary>
    internal sealed class WalkOptions
    {
        public WalkOptions(IReadOnlyDictionary<string, string> vars, Rng rng, string locale, int depth, Budget budget,
            Action<PluralIssue>? onPluralError, bool frozen = false)
        {
            Vars = vars;
            Rng = rng;
            Locale = locale;
            Depth = depth;
            Budget = budget;
            OnPluralError = onPluralError;
            Frozen = frozen;
        }

        public IReadOnlyDictionary<string, string> Vars { get; }

        public Rng Rng { get; }

        /// <summary>Raw locale; normalised per lookup. Empty ⇒ the default 2-form.</summary>
        public string Locale { get; }

        /// <summary>Variable re-processing depth — guards runaway / circular expansion.</summary>
        public int Depth { get; }

        public Budget Budget { get; }

        public Action<PluralIssue>? OnPluralError { get; }

        /// <summary>
        /// Every reference is literal from here down (<c>Renderer.SpliceConstruct</c>). Set for
        /// the subtree of a construct whose textual fixpoint ran out of passes: the plugin runs
        /// ONE fixpoint of 51 passes and then reads text, so whatever it left unexpanded stays
        /// unexpanded — in the body, in a nested construct, in a plural slot. Without this a
        /// leftover would earn a fresh allowance from every walker that met it.
        /// </summary>
        public bool Frozen { get; }

        public WalkOptions WithVars(IReadOnlyDictionary<string, string> vars) =>
            new WalkOptions(vars, Rng, Locale, Depth, Budget, OnPluralError, Frozen);

        public WalkOptions Deeper() => new WalkOptions(Vars, Rng, Locale, Depth + 1, Budget, OnPluralError, Frozen);

        public WalkOptions AsFrozen() => new WalkOptions(Vars, Rng, Locale, Depth, Budget, OnPluralError, true);
    }

    /// <summary>
    /// Port of <c>internal/render.ts</c> — the tree walk with the plugin's staged semantics:
    /// <c>#set</c> is a macro re-rendered at every reference, <c>#def</c> is rolled once against
    /// the full context, conditionals test raw truthiness, plurals expand variables first and
    /// fall back to fullwidth braces, <c>#include</c> is a post-tree line pass. A <c>%var%</c>
    /// that sits DIRECTLY in an enumeration/permutation body is spliced as TEXT and the construct
    /// re-read (<see cref="SpliceConstruct"/>): a <c>|</c> inside such a value separates options,
    /// exactly as in the plugin, whose expansion runs before any bracket is read. The RNG ORDER is
    /// the contract: an enumeration picks before it descends (an unpicked branch never draws),
    /// a permutation renders every element first and draws for size and shuffle after.
    /// </summary>
    internal static class Renderer
    {
        private const int MaxVariableDepth = 50;

        /// <summary>
        /// Characters one render may produce by expanding <c>%variables%</c> (spintax-js#69) —
        /// far above any real document; the point is to end an explosion, not to ration output.
        /// </summary>
        public const int MaxExpansionChars = 1024 * 1024;

        private const string Word = "[A-Za-z0-9_]";
        private static readonly Regex VariableRe = new Regex("%(" + Word + "+)%");
        // ASCII whitespace only — the contract is this regex, pinned by the corpus (extract/include-*).
        private static readonly Regex IncludeLineRe = new Regex(
            JsText.LineStart + @"[ \t]*#include[ \t\n\r\f\x0B]+""([^""]+)""[ \t\n\r\f\x0B]*" + JsText.LineEnd);
        private static readonly Regex IntegerRe = new Regex(@"^-?[0-9]+\z");
        private static readonly Regex LettersOnlyRe = new Regex(@"^\p{L}+\z");

        /// <summary>Build vars → roll <c>#def</c> → walk → resolve includes. Post-process is the pipeline's.</summary>
        public static string RenderAst(ParsedAst ast, RenderCtx ctx)
        {
            var baseVars = BuildVars(ast.SetDefs, ctx.RuntimeContext);
            var walk = new WalkOptions(baseVars, ctx.Rng, ctx.Locale, 0, ctx.Budget, ctx.OnPluralError);
            // The roll happens here, not in BuildVars: a definition is rendered against the FULL
            // context, globals and runtime included, so it must wait until that context exists.
            IReadOnlyDictionary<string, string> vars = baseVars;
            if (ast.DefDefs.Count > 0)
            {
                var merged = new Dictionary<string, string>(baseVars, StringComparer.Ordinal);
                foreach (var kv in RollDefinitions(ast.DefDefs, baseVars, ctx.RuntimeContext, walk)) merged[kv.Key] = kv.Value;
                vars = merged;
            }
            var text = RenderNodes(ast.Nodes, walk.WithVars(vars));
            return ctx.Resolver != null ? ResolveIncludes(text, ctx) : text;
        }

        /// <summary>
        /// Each line-anchored <c>#include "ref"</c> → the host-resolved child template, rendered
        /// with a CHILD scope (inherits runtime context, not the parent's <c>#set</c>). Cycles and
        /// runaway depth resolve to <c>""</c>; a resolver that throws surfaces as
        /// <see cref="IncludeResolverException"/> (programmer error). Cycles are detected by the
        /// ref STRING — two aliases of one template recurse until <c>MaxDepth</c>.
        /// </summary>
        private static string ResolveIncludes(string text, RenderCtx ctx)
        {
            return IncludeLineRe.Replace(text, m =>
            {
                var reference = m.Groups[1].Value;
                if (ctx.IncludeStack.Contains(reference) || ctx.IncludeStack.Count >= ctx.MaxDepth) return "";
                string? included;
                try
                {
                    included = ctx.Resolver!(reference);
                }
                catch (Exception cause)
                {
                    throw new IncludeResolverException($"includeResolver threw for \"{reference}\"", cause);
                }
                if (included is null) return "";
                return RenderAst(Parser.ParseTemplate(included), ctx.ForInclude(reference));
            });
        }

        /// <summary>
        /// The merged variable map: <c>#set</c> values RAW, then the runtime context overlays
        /// them and wins. Context keys are lower-cased. Nothing is resolved here — a <c>#set</c>
        /// is a macro, re-parsed and re-rendered at every reference.
        /// </summary>
        public static Dictionary<string, string> BuildVars(IReadOnlyDictionary<string, string> setDefs,
            IReadOnlyDictionary<string, string> context)
        {
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in setDefs) vars[kv.Key] = kv.Value;
            foreach (var kv in context) vars[kv.Key.ToLowerInvariant()] = kv.Value;
            return vars;
        }

        /// <summary>
        /// Render each <c>#def</c> ONCE, in dependency order (aliases followed through <c>#set</c>
        /// values), against the full context; a runtime variable of the same name outranks a
        /// definition, which is then never rolled at all.
        /// </summary>
        public static Dictionary<string, string> RollDefinitions(IReadOnlyDictionary<string, string> defDefs,
            IReadOnlyDictionary<string, string> vars, IReadOnlyDictionary<string, string> context, WalkOptions opts)
        {
            var outranked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in context.Keys) outranked.Add(key.ToLowerInvariant());
            var rolled = new Dictionary<string, string>(StringComparer.Ordinal);

            // Every macro value a definition can see, minus the definitions that will actually be
            // rolled — a #def shadows a same-named global. A definition the runtime outranks stays:
            // the runtime value is what really gets substituted and the graph must follow it.
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in vars)
            {
                if (defDefs.ContainsKey(kv.Key) && !outranked.Contains(kv.Key)) continue;
                aliases[kv.Key] = kv.Value;
            }

            foreach (var name in OrderDefinitions(defDefs, aliases))
            {
                if (outranked.Contains(name)) continue;
                var value = defDefs.TryGetValue(name, out var v) ? v : "";
                var scope = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in vars) scope[kv.Key] = kv.Value;
                foreach (var kv in rolled) scope[kv.Key] = kv.Value;
                rolled[name] = RenderNodes(Parser.ParseSequence(value), opts.WithVars(scope));
            }

            return rolled;
        }

        /// <summary>Definition names, dependencies first. A cycle cannot be ordered, so its members come last.</summary>
        private static List<string> OrderDefinitions(IReadOnlyDictionary<string, string> defDefs,
            IReadOnlyDictionary<string, string> aliases)
        {
            // The reference iterates Object.keys, whose order is NOT insertion order for every
            // name: a numeric name (`#def %2%`) is an array index to JS and comes first, ascending.
            var names = JsKeyOrder(defDefs.Keys);
            var blocked = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var reached = ReferencedNames(defDefs[name], aliases);
                var deps = new HashSet<string>(StringComparer.Ordinal);
                foreach (var candidate in names)
                    if (reached.Contains(candidate)) deps.Add(candidate);
                blocked[name] = deps;
            }

            var ordered = new List<string>();
            var pending = names;
            while (pending.Count > 0)
            {
                var ready = new List<string>();
                foreach (var name in pending)
                {
                    var waiting = false;
                    foreach (var dep in blocked[name])
                    {
                        if (dep != name && pending.Contains(dep))
                        {
                            waiting = true;
                            break;
                        }
                    }
                    if (!waiting) ready.Add(name);
                }
                if (ready.Count == 0)
                {
                    ordered.AddRange(pending);
                    return ordered;
                }
                ordered.AddRange(ready);
                var next = new List<string>();
                foreach (var name in pending)
                    if (!ready.Contains(name)) next.Add(name);
                pending = next;
            }
            return ordered;
        }

        /// <summary>
        /// <c>Object.keys</c> order: canonical array-index keys ("0" … "4294967294", no leading
        /// zero) first, ascending numerically; every other key after them in insertion order.
        /// The roll order of definitions is observable through the RNG, so it has to match.
        /// </summary>
        internal static List<string> JsKeyOrder(IEnumerable<string> insertionOrder)
        {
            var indices = new List<(uint index, string key)>();
            var rest = new List<string>();
            foreach (var key in insertionOrder)
            {
                if (IsArrayIndex(key, out var index)) indices.Add((index, key));
                else rest.Add(key);
            }
            indices.Sort((a, b) => a.index.CompareTo(b.index));
            var ordered = new List<string>(indices.Count + rest.Count);
            foreach (var (_, key) in indices) ordered.Add(key);
            ordered.AddRange(rest);
            return ordered;
        }

        private static bool IsArrayIndex(string key, out uint index)
        {
            index = 0;
            if (key.Length == 0 || key.Length > 10) return false;
            if (key.Length > 1 && key[0] == '0') return false;
            foreach (var ch in key)
                if (ch < '0' || ch > '9') return false;
            return ulong.TryParse(key, out var v) && v <= 4294967294UL && (index = (uint)v) == v;
        }

        /// <summary>Every variable name a value reaches, hopping through macro (alias) values to a fixpoint.</summary>
        private static HashSet<string> ReferencedNames(string value, IReadOnlyDictionary<string, string> aliases)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(DirectReferences(value));
            while (queue.Count > 0)
            {
                var name = queue.Dequeue();
                if (!seen.Add(name)) continue;
                if (aliases.TryGetValue(name, out var alias))
                    foreach (var r in DirectReferences(alias)) queue.Enqueue(r);
            }
            return seen;
        }

        /// <summary>The <c>%var%</c> names written literally in a string, lower-cased.</summary>
        private static List<string> DirectReferences(string text)
        {
            var names = new List<string>();
            foreach (Match m in VariableRe.Matches(text)) names.Add(m.Groups[1].Value.ToLowerInvariant());
            return names;
        }

        // ── the walk ─────────────────────────────────────────────────────────────────────

        /// <summary>Child lists being rendered for the construct a frame paused on, and how to assemble them.</summary>
        private sealed class Pending
        {
            public Pending(IReadOnlyList<IReadOnlyList<Node>> lists, Func<List<string>, string> assemble)
            {
                Lists = lists;
                Assemble = assemble;
            }

            public IReadOnlyList<IReadOnlyList<Node>> Lists { get; }

            public List<string> Done { get; } = new List<string>();

            public Func<List<string>, string> Assemble { get; }

            /// <summary>The common shape: one child list, emitted as is.</summary>
            public static Pending Single(IReadOnlyList<Node> list) =>
                new Pending(new[] { list }, parts => parts.Count > 0 ? parts[0] : "");
        }

        private sealed class Frame
        {
            public Frame(IReadOnlyList<Node> nodes) { Nodes = nodes; }

            public IReadOnlyList<Node> Nodes { get; }

            public int I;
            public readonly StringBuilder Out = new StringBuilder();
            public Pending? Pending;
        }

        /// <summary>
        /// Walk a node list, iteratively (#68). Children are rendered at exactly the moment the
        /// recursive version rendered them — the RNG order is observable and pinned by fixtures.
        /// </summary>
        public static string RenderNodes(IReadOnlyList<Node> nodes, WalkOptions opts)
        {
            var stack = new Stack<Frame>();
            stack.Push(new Frame(nodes));

            while (stack.Count > 0)
            {
                var f = stack.Peek();

                if (f.Pending != null)
                {
                    if (f.Pending.Done.Count < f.Pending.Lists.Count)
                    {
                        stack.Push(new Frame(f.Pending.Lists[f.Pending.Done.Count]));
                        continue;
                    }
                    f.Out.Append(f.Pending.Assemble(f.Pending.Done));
                    f.Pending = null;
                    continue;
                }

                if (f.I >= f.Nodes.Count)
                {
                    var text = f.Out.ToString();
                    stack.Pop();
                    if (stack.Count == 0) return text;
                    stack.Peek().Pending!.Done.Add(text);
                    continue;
                }

                var node = f.Nodes[f.I];
                f.I++;
                var (step, pending) = RenderNode(node, opts);
                if (pending != null) f.Pending = pending;
                else f.Out.Append(step);
            }

            return "";
        }

        /// <summary>What a node contributes: finished text, or child lists plus how to assemble them.</summary>
        private static (string text, Pending? pending) RenderNode(Node node, WalkOptions opts)
        {
            switch (node)
            {
                case LiteralNode l:
                    return (l.Value, null);
                case VariableNode v:
                    return ResolveVariable(v.Name, opts);
                case EnumerationNode e:
                    return RenderEnumeration(e, opts);
                case PermutationNode p:
                    return RenderPermutation(p, opts);
                case ConditionalNode c:
                    return ("", Pending.Single(ConditionalTakesThen(c.Name, c.Inverted, opts) ? c.Then : c.Else));
                case PluralNode pl:
                    return RenderPlural(pl, opts);
                default:
                    return ("", null);
            }
        }

        /// <summary><c>min == max</c> short-circuits WITHOUT consuming the RNG (plugin <c>random_int</c>).</summary>
        private static int RandomInt(Rng rng, int min, int max) => min == max ? min : rng(min, max);

        /// <summary>
        /// A value with constructs is re-parsed and rendered (depth-capped); a plain value is
        /// returned as is; an unresolved name stays verbatim. Out of budget ⇒ the reference stays
        /// literal, exactly as an undefined name does — render never throws on content.
        /// </summary>
        private static (string, Pending?) ResolveVariable(string name, WalkOptions opts)
        {
            if (opts.Frozen || !opts.Vars.TryGetValue(name.ToLowerInvariant(), out var value)) return ("%" + name + "%", null);
            // The budget is checked BEFORE the plain-value shortcut, so every substitution is
            // charged, as it is in the plugin. A plain value used to be free — harmless while it
            // could only be a leaf, and the one door left open once a re-read construct
            // (SpliceConstruct) could hand this function references its fixpoint had cut off:
            // 2^k of them, each to a 64 KB value, expanded here for nothing (reference review).
            if (opts.Budget.Left <= 0) return ("%" + name + "%", null);
            opts.Budget.Left -= value.Length;
            if (opts.Depth >= MaxVariableDepth || !ContainsAnyOf(value, "{[%")) return (value, null);
            // ParseSequence, NOT ParseTemplate: a value is not re-comment-stripped or re-#set-extracted.
            return (RenderNodes(Parser.ParseSequence(value), opts.Deeper()), null);
        }

        /// <summary>
        /// The passes a textual fixpoint may run from this point of the walk: the plugin's loop
        /// is <c>&lt;= MAX_VARIABLE_DEPTH</c> — 51 passes, once, over the whole text — and a
        /// construct or slot reached through a macro re-parse has already spent <c>Depth</c> of
        /// those hops in <see cref="ResolveVariable"/>. Never below one.
        /// </summary>
        private static int PassesLeft(WalkOptions opts) => Math.Max(1, MaxVariableDepth - opts.Depth + 1);

        /// <summary>The outcome of <see cref="ExpandVarsFixpoint"/>.</summary>
        private struct Fixpoint
        {
            public string Text;
            public bool Converged;
        }

        private static bool ContainsAnyOf(string s, string set)
        {
            foreach (var ch in s)
                if (set.IndexOf(ch) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Variable expansion ONLY (plugin <c>expand_variables</c> fixpoint) — enums/perms stay
        /// literal — with the fact a caller may need: whether a pass came back unchanged before
        /// the pass budget ran out. Not converged means the text was still changing on the last
        /// allowed pass — a cycle or a chain deeper than the budget — and the caller must then
        /// keep every leftover reference literal (<see cref="WalkOptions.Frozen"/>), because the
        /// plugin never expands again after its one fixpoint.
        /// </summary>
        private static Fixpoint ExpandVarsFixpoint(string text, WalkOptions opts, int passes)
        {
            if (opts.Frozen) return new Fixpoint { Text = text, Converged = true };
            var output = text;
            for (var i = 0; i < passes; i++)
            {
                var changed = false;
                output = VariableRe.Replace(output, m =>
                {
                    if (!opts.Vars.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var value)) return m.Value;
                    // Same purse as ResolveVariable: a plural slot is not a separate allowance.
                    if (opts.Budget.Left <= 0) return m.Value;
                    opts.Budget.Left -= value.Length;
                    changed = true;
                    return value;
                });
                if (!changed) return new Fixpoint { Text = output, Converged = true };
            }
            return new Fixpoint { Text = output, Converged = false };
        }

        /// <summary>Truthy = the raw var value is set and has a non-whitespace char (plugin <c>is_truthy</c>; JS <c>\S</c>).</summary>
        private static bool ConditionalTakesThen(string name, bool inverted, WalkOptions opts)
        {
            var truthy = opts.Vars.TryGetValue(name.ToLowerInvariant(), out var value) && HasNonWhitespace(value);
            return inverted ? !truthy : truthy;
        }

        private static bool HasNonWhitespace(string s)
        {
            foreach (var ch in s)
                if (!JsText.IsJsWhiteSpace(ch)) return true;
            return false;
        }

        /// <summary>
        /// Resolve conditionals in a piece of text textually — the taken branch is substituted,
        /// never rendered. Three callers: the plural COUNT slot (spintax-js#67, where this was
        /// born: enums and permutations resolve AFTER plurals, so a branch yielding <c>{a|b}</c>
        /// must reach the numeric test as such and erase the block), the body of a construct
        /// being re-read after a direct <c>%var%</c> splice (<see cref="SpliceConstruct"/>), which
        /// needs the plugin's Stage 6a/6c around its expansion for the same reason, and
        /// <see cref="Census"/>, which must model that same splice to count what this renders.
        /// Iterative over spans, linear.
        /// </summary>
        /// <param name="takesThen">
        /// Name and <c>inverted</c> ⇒ is the <c>then</c> branch taken. A delegate, not a
        /// <see cref="WalkOptions"/>, so the census can answer from its own row without a second
        /// copy of the conditional grammar living next to it.
        /// </param>
        internal static string ResolveConditionalsInText(string text, Func<string, bool, bool> takesThen)
        {
            if (text.IndexOf("{?", StringComparison.Ordinal) < 0) return text;

            var close = MatchBraces(text);
            var output = new StringBuilder();
            var pending = new Stack<(int from, int to)>();
            pending.Push((0, text.Length));

            while (pending.Count > 0)
            {
                var (i, segEnd) = pending.Pop();
                while (i < segEnd)
                {
                    var open = text.IndexOf("{?", i, StringComparison.Ordinal);
                    // A `{?` found past this span belongs to the text around it.
                    if (open < 0 || open + 1 >= segEnd)
                    {
                        output.Append(text, i, segEnd - i);
                        break;
                    }

                    // A close outside the span is no close at all.
                    var shut = close[open];
                    var head = shut < 0 || shut >= segEnd ? null : Parser.RecognizeConditional(text, open + 1, shut);
                    if (head is null)
                    {
                        var upto = Math.Min(open + 2, segEnd);
                        output.Append(text, i, upto - i);
                        i = open + 2;
                        continue;
                    }

                    output.Append(text, i, open - i);
                    var branchEnd = head.SepIndex < 0 ? shut : head.SepIndex;
                    var (from, to) = takesThen(head.Name, head.Inverted)
                        ? (head.BodyStart, branchEnd)
                        : (head.SepIndex < 0 ? shut : head.SepIndex + 1, shut);
                    // Continuation first, branch second: the stack pops the branch back out ahead
                    // of it, which keeps the output in source order.
                    pending.Push((shut + 1, segEnd));
                    pending.Push((from, to));
                    break;
                }
            }

            return output.ToString();
        }

        /// <summary>The closing brace for every opening one, or -1 — one pass, so an unbalanced count slot stays linear.</summary>
        private static int[] MatchBraces(string text)
        {
            var close = new int[text.Length];
            for (var k = 0; k < close.Length; k++) close[k] = -1;
            var opens = new Stack<int>();
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '{') opens.Push(i);
                else if (ch == '}' && opens.Count > 0) close[opens.Pop()] = i;
            }
            return close;
        }

        /// <summary>
        /// Plural agreement (Stage 6d — after variable expansion, before enum/perm). Order:
        /// bracket check → numeric erase → arity → bucket pick. The two error paths emit the
        /// var-expanded construct with fullwidth braces.
        /// </summary>
        private static (string, Pending?) RenderPlural(PluralNode node, WalkOptions opts)
        {
            // Both slots get the same pass arithmetic as a re-read construct (51 hops in every
            // shape), and a form list whose passes ran out renders its pick FROZEN: a flat 50 and
            // an unfrozen pick let a 52-deep chain in a form resolve to its end where the plugin
            // leaves `%a52%` (reference review).
            var passes = PassesLeft(opts);
            var countPass = ExpandVarsFixpoint(node.CountRaw, opts, passes);
            var formsPass = ExpandVarsFixpoint(node.FormsRaw, opts, passes);
            var countRaw = ResolveConditionalsInText(countPass.Text, (n, inv) => ConditionalTakesThen(n, inv, opts));
            var formsRaw = formsPass.Text;
            var baseLang = Plurals.NormalizeBaseLang(opts.Locale);

            if (ContainsAnyOf(formsRaw, "{}[]"))
            {
                Report(opts, new PluralIssue("plural.nested-brackets",
                    "Plural form slot contains nested spintax brackets; extract via #def first — a #set is "
                    + "substituted verbatim and would put the brackets straight back.",
                    RawConstruct(countRaw, formsRaw), baseLang, null, null));
                return (FullwidthVerbatim(countRaw, formsRaw), null);
            }

            var count = Parser.PhpTrim(countRaw);
            if (!IntegerRe.IsMatch(count))
            {
                // Erasing leaves no trace, so this report is the ONLY way a host can tell an
                // intentionally empty sentence from an unsubstituted %Var%.
                Report(opts, new PluralIssue("plural.count",
                    "Plural count slot is empty or non-numeric (" + JsQuote(count) + "); block erased.",
                    RawConstruct(countRaw, formsRaw), baseLang, null, null));
                return ("", null);
            }

            var rawForms = formsRaw.Split('|');
            var forms = new string[rawForms.Length];
            for (var i = 0; i < rawForms.Length; i++) forms[i] = Parser.PhpTrim(rawForms[i]);
            var arity = Plurals.PluralArity(baseLang);
            if (forms.Length != arity)
            {
                Report(opts, new PluralIssue("plural.arity",
                    $"Plural has {forms.Length} form(s); locale \"{baseLang}\" takes {arity}.",
                    RawConstruct(countRaw, formsRaw), baseLang, arity, forms.Length));
                return (FullwidthVerbatim(countRaw, formsRaw), null);
            }

            // The picked form re-enters the pipeline (its enums/perms resolve after plurals) — as
            // a child list, so a deeply nested form does not cost a stack frame.
            var picked = Plurals.PluralFor(baseLang, ParseCount(count), forms);
            if (!formsPass.Converged) return (RenderNodes(Parser.ParseSequence(picked), opts.AsFrozen()), null);
            return ("", Pending.Single(Parser.ParseSequence(picked)));
        }

        /// <summary>
        /// JS <c>Number.parseInt</c> of a digit run: a double, exact to 2^53, then the nearest
        /// double, then ±Infinity. <c>TryParse</c>, never <c>Parse</c>: on .NET Framework — the
        /// runtime under ZennoPoster — <c>double.Parse</c> THROWS on a run past the double range,
        /// and render never throws on content. The digits are already known to be <c>-?[0-9]+</c>.
        /// </summary>
        private static double ParseCount(string digits)
        {
            if (double.TryParse(digits, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            return digits.Length > 0 && digits[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
        }

        private static void Report(WalkOptions opts, PluralIssue issue) => opts.OnPluralError?.Invoke(issue);

        /// <summary>The construct as the renderer saw it — ASCII braces, for reports.</summary>
        private static string RawConstruct(string countRaw, string formsRaw) => "{plural " + countRaw + ":" + formsRaw + "}";

        /// <summary>Verbatim with fullwidth braces (U+FF5B/U+FF5D) so later passes leave it alone.</summary>
        private static string FullwidthVerbatim(string countRaw, string formsRaw) =>
            RawConstruct(countRaw, formsRaw).Replace('{', '｛').Replace('}', '｝');

        /// <summary><c>JSON.stringify</c> of a short string, for a message: the short escapes, <c>\uXXXX</c> for other controls and for lone surrogates (well-formed stringify).</summary>
        private static string JsQuote(string s)
        {
            var sb = new StringBuilder("\"");
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                        {
                            sb.Append(ch).Append(s[i + 1]);
                            i++;
                        }
                        else if (char.IsSurrogate(ch)) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Pick one option (outer-first) and render it; the pick happens BEFORE the descent.</summary>
        private static (string, Pending?) RenderEnumeration(EnumerationNode node, WalkOptions opts)
        {
            if (node.Raw != null && SpliceConstruct(node.Raw, '{', '}', opts, out var spliced)) return spliced;
            var options = node.Options;
            if (options.Count == 0) return ("", null);
            var picked = options[RandomInt(opts.Rng, 0, options.Count - 1)];
            return ("", Pending.Single(picked));
        }

        /// <summary>
        /// Splice the direct <c>%var%</c> references of a construct into its body as TEXT and
        /// re-read the construct — the plugin's own order (Stage 6a conditionals → 6b expansion →
        /// 6c conditionals) run over this one body, then the brackets go back on and the parser
        /// reads the result. Only constructs the parser marked (<c>Raw</c>) get here; every other
        /// one keeps the tree it was parsed into, and with it the exact RNG order the corpus pins.
        /// </summary>
        /// <remarks>
        /// Why textual: <c>[&lt;…&gt;%list%]</c> with <c>%list% = a|b|c</c> is ONE option to the
        /// parser, because the tree is built before any value exists, and
        /// <see cref="ResolveVariable"/> hands a construct-free value back as finished text — so
        /// the <c>|</c> that separates elements in every PHP engine was never seen here, and a
        /// 57-name list rendered as one element (spintax-dotnet#1). Same for <c>{%list%}</c>.
        /// <para>
        /// Returns <c>false</c> when the body would not change — an undefined name, a reference
        /// the budget cut off — so the caller renders the nodes it already has. That is also what
        /// terminates the re-read: after a converged fixpoint every reference left is one
        /// expansion cannot touch, so a re-read construct changes nothing and falls through.
        /// </para>
        /// <para>
        /// Hop budget: the plugin's fixpoint is 51 passes and it runs once, over text; a construct
        /// reached through a macro re-parse has already spent <c>Depth</c> of those hops, so it
        /// gets <c>51 - Depth</c> passes here and the total is 51 in every shape. When the passes
        /// run out still changing, whatever is left is FROZEN for the whole subtree: the mutual
        /// cycle leaves <c>%b%</c>, a 51-deep chain into <c>x|y</c> reaches the body as text and
        /// IS split — inside a bracket exactly as outside one — and nothing below earns a fresh
        /// allowance.
        /// </para>
        /// </remarks>
        private static bool SpliceConstruct(string raw, char open, char close, WalkOptions opts, out (string, Pending?) result)
        {
            result = ("", null);
            if (opts.Frozen) return false;
            Func<string, bool, bool> takesThen = (n, inv) => ConditionalTakesThen(n, inv, opts);
            var expanded = ExpandVarsFixpoint(ResolveConditionalsInText(raw, takesThen), opts, PassesLeft(opts));
            var body = ResolveConditionalsInText(expanded.Text, takesThen);
            if (body == raw) return false;
            // The brackets go back on so an unbalanced value degrades exactly as the plugin's
            // innermost regex does: `{a}b}` is `a` followed by the literal `b}`, in both engines.
            var nodes = Parser.ParseSequence(open + body + close);
            result = expanded.Converged
                ? ("", Pending.Single(nodes))
                : (RenderNodes(nodes, opts.AsFrozen()), null);
            return true;
        }

        private sealed class Element
        {
            public Element(string text, string? sep)
            {
                Text = text;
                Sep = sep;
            }

            public string Text { get; }

            public string? Sep { get; }
        }

        private static (string, Pending?) RenderPermutation(PermutationNode node, WalkOptions opts)
        {
            if (node.Raw != null && SpliceConstruct(node.Raw, '[', ']', opts, out var spliced)) return spliced;
            if (node.Options.Count == 0) return ("", null);
            var lists = new IReadOnlyList<Node>[node.Options.Count];
            for (var i = 0; i < lists.Length; i++) lists[i] = node.Options[i].Nodes;
            return ("", new Pending(lists, parts => AssemblePermutation(node, parts, opts)));
        }

        /// <summary>Shuffle and join once every element is rendered — the size pick and the shuffle draw AFTER the children.</summary>
        private static string AssemblePermutation(PermutationNode node, List<string> rendered, WalkOptions opts)
        {
            var elements = new List<Element>(node.Options.Count);
            for (var i = 0; i < node.Options.Count; i++)
                elements.Add(new Element(i < rendered.Count ? rendered[i] : "", node.Options[i].Separator));
            var total = elements.Count;
            if (total == 0) return "";

            var config = node.Config;
            int min, max;
            if (config.MinSize.HasValue && config.MaxSize.HasValue)
            {
                min = config.MinSize.Value;
                max = config.MaxSize.Value;
            }
            else if (config.MinSize.HasValue)
            {
                min = config.MinSize.Value;
                max = total;
            }
            else if (config.MaxSize.HasValue)
            {
                min = 1;
                max = config.MaxSize.Value;
            }
            else
            {
                min = total;
                max = total;
            }
            min = Math.Max(1, Math.Min(min, total));
            max = Math.Max(min, Math.Min(max, total));

            var pick = RandomInt(opts.Rng, min, max);
            Shuffle(elements, opts.Rng);
            return JoinWithSeparators(elements, pick, config.Sep, config.LastSep ?? config.Sep);
        }

        /// <summary>Fisher-Yates, matching the plugin: i = n-1 … 1, j = randomInt(0, i), swap.</summary>
        private static void Shuffle(List<Element> arr, Rng rng)
        {
            for (var i = arr.Count - 1; i > 0; i--)
            {
                var j = RandomInt(rng, 0, i);
                var tmp = arr[i];
                arr[i] = arr[j];
                arr[j] = tmp;
            }
        }

        private static string JoinWithSeparators(List<Element> elements, int count, string globalSep, string globalLastSep)
        {
            if (count == 0) return "";
            if (count == 1) return elements[0].Text;

            var sb = new StringBuilder(elements[0].Text);
            for (var i = 1; i < count; i++)
            {
                var el = elements[i];
                var sep = el.Sep ?? (i == count - 1 ? globalLastSep : globalSep);
                sb.Append(PadSeparator(sep)).Append(el.Text);
            }
            return sb.ToString();
        }

        /// <summary>Purely alphabetic separators get space-padded; others pass through (plugin).</summary>
        private static string PadSeparator(string sep)
        {
            var trimmed = Parser.PhpTrim(sep);
            if (trimmed.Length == 0) return sep;
            if (LettersOnlyRe.IsMatch(trimmed)) return " " + trimmed + " ";
            return sep;
        }
    }
}
