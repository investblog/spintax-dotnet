using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Spintax.Core
{
    /// <summary>
    /// How much a template can say — counted, not sampled (docs/PROPOSAL-quality-tooling.md).
    /// <see cref="Combinations"/> is the exact number of distinct CHOICE PATHS the tree allows —
    /// the number of different texts when no two options spell the same thing (<c>{a|a}</c>
    /// counts 2, as the engine would draw it); <see cref="MaxLength"/> the length of the
    /// longest render, before post-process.
    /// </summary>
    /// <remarks>
    /// Two readings, chosen by whether a variable map is given:
    /// <list type="bullet">
    /// <item><b>For one row of data</b> (<c>vars</c> given): a conditional takes the branch the
    /// data takes, a plural the form the count takes — the number of different texts ONE product
    /// can get.</item>
    /// <item><b>Across all data</b> (<c>vars</c> null): a conditional counts both branches, a
    /// plural every form.</item>
    /// </list>
    /// A <c>#set</c> re-rolls at every reference and simply multiplies. A <c>#def</c> rolls once
    /// per document, so it multiplies once — and only the alternatives that actually reference it
    /// see its variety. The walk therefore carries, for every sub-tree, a polynomial: "the set of
    /// defs this alternative references" → "how many ways". At the end each referenced def is
    /// expanded by substituting ITS polynomial into the term (a def that references another def
    /// shares that def's roll — <c>#def %a% = %b%</c> does not double anything), until no
    /// reference is left. Permutations: for each allowed size <c>k</c>, every ordered subset —
    /// <c>k!·e_k(c₁…cₙ)</c> — over the elements that SURVIVE, because an element whose rendered
    /// text is blank is dropped and the size clamp follows the survivors (spintax-js#80), so the
    /// walk carries a second dimension for how many were dropped. Plurals follow the renderer's stage: brackets in the forms or the
    /// wrong arity emit the construct verbatim (one outcome); for a row, a non-numeric count
    /// erases the block and a count that depends on a <c>#def</c> roll counts every form. Values
    /// from the row that contain constructs are re-parsed, as the renderer re-parses them. Counts
    /// and lengths saturate at <see cref="long.MaxValue"/>. The longest permutation is exact
    /// for global separators; with per-element separators the longest of them stands in for every
    /// slot, and where an element can render blank the full element list is measured — both upper
    /// bounds, because dropping only shortens.
    /// <para>
    /// Two of the renderer's caps ARE mirrored: the variable cap applies to variable hops only
    /// (rolling a definition is not one), and a fixpoint that ran out of passes freezes its
    /// subtree — a reference left over is then literal text, and counts as the length of
    /// <c>%name%</c>.
    /// </para>
    /// <para>
    /// <b>Where this walk cannot bound an answer it SATURATES</b> at <see cref="long.MaxValue"/>
    /// rather than report a number built on a sub-tree it abandoned: the expansion allowance
    /// running out anywhere, or the structural backstop being reached. That also ends a doubling
    /// macro, which used to walk 2^50 nodes and kill the process.
    /// </para>
    /// <para>
    /// It is <b>not</b> a proof of an upper bound, and must not be documented as one. This is a
    /// static walk of a tree describing a dynamic engine; they part company wherever the render
    /// stops expanding — an unexpanded reference emits <c>%name%</c>, which can be LONGER than the
    /// value it replaced, and mutually exclusive branches each deserve the allowance the other
    /// spent — and at several places inside the allowance too. The allowance is charged at most of
    /// the substitutions the renderer charges, not provably all of them. Every known gap is
    /// measured and listed in <c>docs/TODO.md</c>: a <c>#def</c> cycle's length, a
    /// construct-bearing <c>#def</c> spliced into a construct, <c>#include</c>d children (which
    /// these entry points never see — they take no resolver), directive-backed variables in a
    /// plural slot or a conditional test, and the per-element separator approximation above. A
    /// template that uses none of them and stays inside the allowance is counted exactly, which is
    /// every ordinary one and what the tests pin.
    /// </para>
    /// </remarks>
    internal static class Census
    {
        public static long Combinations(ParsedAst ast, IReadOnlyDictionary<string, string>? vars, string? locale)
        {
            var w = new Walker(ast, vars, locale);
            w.RollDefinitions();
            var poly = w.Sequence(ast.Nodes).poly;
            var total = w.Expand(poly);
            // AFTER Expand, not before: substituting the referenced definitions walks their bodies,
            // which charges and can reach the backstop on its own. Checking first returned a finite
            // total built on variables that had already collapsed to one.
            if (w.Unbounded) return long.MaxValue;
            return total > long.MaxValue ? long.MaxValue : (long)total;
        }

        public static long MaxLength(ParsedAst ast, IReadOnlyDictionary<string, string>? vars, string? locale)
        {
            var w = new Walker(ast, vars, locale);
            w.RollDefinitions();
            var length = w.Sequence(ast.Nodes).length;
            return w.Unbounded ? long.MaxValue : length;
        }

        private static readonly Regex IntegerRe = new Regex(@"^-?[0-9]+\z");
        private static readonly Regex VariableRe = new Regex("%([A-Za-z0-9_]+)%");

        private static long SatAdd(long a, long b) => b > long.MaxValue - a ? long.MaxValue : a + b;

        private static long SatMul(long a, long b)
        {
            if (a == 0 || b == 0) return 0;
            return a > long.MaxValue / b ? long.MaxValue : a * b;
        }

        private static bool ContainsAnyOf(string s, string set)
        {
            foreach (var ch in s)
                if (set.IndexOf(ch) >= 0) return true;
            return false;
        }

        /// <summary>Set algebra on the sorted, comma-joined def-name keys ("" = the empty set).</summary>
        private static string Union(string a, string b)
        {
            if (a.Length == 0) return b;
            if (b.Length == 0) return a;
            var set = new SortedSet<string>(a.Split(','), StringComparer.Ordinal);
            foreach (var s in b.Split(',')) set.Add(s);
            return string.Join(",", set);
        }

        private static string Minus(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return a;
            var set = new SortedSet<string>(a.Split(','), StringComparer.Ordinal);
            foreach (var s in b.Split(',')) set.Remove(s);
            return string.Join(",", set);
        }

        /// <summary>ways, keyed by the sorted set of #def names an alternative references ("" = none).</summary>
        private sealed class Poly : Dictionary<string, BigInteger>
        {
            public Poly() : base(StringComparer.Ordinal) { }

            public static Poly One() => new Poly { [""] = BigInteger.One };

            public static Poly Ref(string def) => new Poly { [def] = BigInteger.One };

            public static Poly Mul(Poly a, Poly b)
            {
                var r = new Poly();
                foreach (var ka in a)
                    foreach (var kb in b)
                    {
                        var key = Union(ka.Key, kb.Key);
                        r.TryGetValue(key, out var cur);
                        r[key] = cur + ka.Value * kb.Value;
                    }
                return r;
            }

            public static Poly Add(Poly a, Poly b)
            {
                var r = new Poly();
                foreach (var kv in a) r[kv.Key] = kv.Value;
                foreach (var kv in b)
                {
                    r.TryGetValue(kv.Key, out var cur);
                    r[kv.Key] = cur + kv.Value;
                }
                return r;
            }

            public static Poly Scale(Poly a, BigInteger factor)
            {
                var r = new Poly();
                foreach (var kv in a) r[kv.Key] = kv.Value * factor;
                return r;
            }

            /// <summary>
            /// <paramref name="b"/> taken out of <paramref name="a"/>, term by term — the ways a
            /// permutation element does NOT render blank. Only ever called with a <paramref name="b"/>
            /// built from <paramref name="a"/>'s own structure, so a term cannot go negative; a
            /// term that reaches zero is dropped, which keeps "no ways" spelled one way (an
            /// empty poly) for <see cref="IsZero"/>.
            /// </summary>
            public static Poly Sub(Poly a, Poly b)
            {
                var r = new Poly();
                foreach (var kv in a)
                {
                    b.TryGetValue(kv.Key, out var minus);
                    var left = kv.Value - minus;
                    if (left > BigInteger.Zero) r[kv.Key] = left;
                }
                return r;
            }

            public static bool IsZero(Poly a) => a.Count == 0;
        }

        private sealed class Walker
        {
            private readonly ParsedAst _ast;
            private readonly Dictionary<string, string> _lowerVars = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>
            /// The renderer's textual fixpoint runs <c>51 - depth</c> passes and this walk starts
            /// at depth 0 — a count that stopped one hop earlier than the render it describes
            /// would erase a plural block the engine resolves.
            /// </summary>
            private const int Passes = 51;

            /// <summary>Characters of textual expansion left for this walk — the renderer's allowance, for the renderer's reason.</summary>
            private long _spliceBudget = Renderer.MaxExpansionChars;

            /// <summary>
            /// The allowance ran out somewhere in this walk. It does not mean a RENDER's did — this
            /// walk visits every branch while a render charges down one — only that the counting
            /// from here on rests on references this walk stopped expanding. Past that point the
            /// numbers describe no particular render, so the entry points saturate instead.
            /// </summary>
            private bool _budgetExceeded;

            /// <summary>The structural backstop was reached, so a sub-tree was abandoned mid-count.</summary>
            private bool _capReached;

            /// <summary>
            /// This walk cannot bound the answer: it either ran out of allowance or hit the
            /// backstop, and both entry points saturate rather than report a number the engine
            /// can contradict.
            /// </summary>
            public bool Unbounded => _budgetExceeded || _capReached;

            /// <summary>
            /// Inside the subtree of a re-read whose fixpoint ran out of passes, as the renderer's
            /// <c>WalkOptions.Frozen</c>: every reference left is literal text and nothing below
            /// earns a fresh allowance.
            /// </summary>
            private bool _frozen;

            /// <summary>
            /// The RENDERER's variable-expansion depth, which is not this walk's recursion depth:
            /// <c>ResolveVariable</c> goes <c>Deeper()</c> only when a VALUE is re-parsed, so a
            /// <c>#def</c> roll and a re-read stay at the depth they were reached at.
            /// <see cref="Guarded"/> counts every descent — using it for the pass arithmetic gave
            /// a definition 50 passes where the render gives 51.
            /// </summary>
            private int _varDepth;

            /// <summary>The passes a textual fixpoint may run from here — the renderer's <c>PassesLeft</c>.</summary>
            private int PassesLeft => Math.Max(1, Passes - _varDepth);
            private readonly bool _forRow;
            private readonly string _baseLang;
            private readonly Dictionary<string, Poly> _defPolys = new Dictionary<string, Poly>(StringComparer.Ordinal);
            private readonly Dictionary<string, long> _defLengths = new Dictionary<string, long>(StringComparer.Ordinal);
            private int _depth;

            /// <summary>
            /// Roll every definition once, as <c>RollDefinitions</c> does before the walk — even
            /// one nothing references, which this walk would otherwise never visit and never
            /// charge. The allowance must cover what the RENDER spends, or "stayed inside it"
            /// proves nothing. A definition the row outranks is never rolled, there or here.
            /// </summary>
            public void RollDefinitions()
            {
                foreach (var name in _ast.DefDefs.Keys)
                    if (!_lowerVars.ContainsKey(name))
                        DefLength(name);
            }

            public Walker(ParsedAst ast, IReadOnlyDictionary<string, string>? vars, string? locale)
            {
                _ast = ast;
                _forRow = vars != null;
                if (vars != null)
                    foreach (var kv in vars) _lowerVars[kv.Key.ToLowerInvariant()] = kv.Value;
                _baseLang = Plurals.NormalizeBaseLang(locale);
            }

            /// <summary>Σ over the terms, each with its referenced defs substituted out.</summary>
            public BigInteger Expand(Poly poly)
            {
                BigInteger total = BigInteger.Zero;
                foreach (var kv in poly) total += ExpandTerm(kv.Key, kv.Value, "");
                return total;
            }

            /// <summary>
            /// One term: <c>ways</c> alternatives that reference the defs in <c>pending</c>. Take
            /// the first, substitute its polynomial (each of ITS terms brings the defs it
            /// references — minus those already substituted on this path, so a cycle or a shared
            /// dependency is rolled once), and recurse until nothing is pending.
            /// </summary>
            private BigInteger ExpandTerm(string pending, BigInteger ways, string done)
            {
                if (pending.Length == 0) return ways;
                var comma = pending.IndexOf(',');
                var def = comma < 0 ? pending : pending.Substring(0, comma);
                var rest = comma < 0 ? "" : pending.Substring(comma + 1);
                var doneNow = Union(done, def);
                BigInteger total = BigInteger.Zero;
                foreach (var kv in DefPoly(def))
                    total += ExpandTerm(Union(rest, Minus(kv.Key, doneNow)), ways * kv.Value, doneNow);
                return total;
            }

            /// <summary>The polynomial of a #def's own value — its choices, and the defs it references.</summary>
            private Poly DefPoly(string name)
            {
                if (_defPolys.TryGetValue(name, out var cached)) return cached;
                var poly = Guarded(() => Sequence(Parser.ParseSequence(_ast.DefDefs[name])).poly, Poly.One());
                _defPolys[name] = poly;
                return poly;
            }

            private long DefLength(string name)
            {
                if (_defLengths.TryGetValue(name, out var cached)) return cached;
                _defLengths[name] = 0; // a cycle contributes nothing beyond its first pass
                var length = Guarded(() => Sequence(Parser.ParseSequence(_ast.DefDefs[name])).length, 0L);
                _defLengths[name] = length;
                return length;
            }

            public (Poly poly, long length) Sequence(IReadOnlyList<Node> nodes)
            {
                var poly = Poly.One();
                long length = 0;
                foreach (var n in nodes)
                {
                    var (p, l) = Node(n);
                    poly = Poly.Mul(poly, p);
                    length = SatAdd(length, l);
                }
                return (poly, length);
            }

            private (Poly, long) Node(Node node)
            {
                switch (node)
                {
                    case LiteralNode lit:
                        return (Poly.One(), lit.Value.Length);
                    case VariableNode v:
                        return Variable(v.Name);
                    case EnumerationNode e:
                        {
                            var re = ReRead(e, e.Raw, '{', '}');
                            if (re != null) return re.Value;
                            var poly = new Poly();
                            long length = 0;
                            foreach (var option in e.Options)
                            {
                                var (p, l) = Sequence(option);
                                poly = Poly.Add(poly, p);
                                length = Math.Max(length, l);
                            }
                            return (poly, length);
                        }
                    case ConditionalNode c:
                        {
                            if (_forRow) return Sequence(TakesThen(c.Name, c.Inverted) ? c.Then : c.Else);
                            var (tp, tl) = Sequence(c.Then);
                            var (ep, el) = Sequence(c.Else);
                            return (Poly.Add(tp, ep), Math.Max(tl, el));
                        }
                    case PluralNode p:
                        return Plural(p);
                    case PermutationNode perm:
                        {
                            var re = ReRead(perm, perm.Raw, '[', ']');
                            return re ?? Permutation(perm);
                        }
                    default:
                        return (Poly.One(), 0);
                }
            }

            /// <summary>
            /// The renderer's splice, mirrored (<c>Renderer.SpliceConstruct</c>): a construct the
            /// parser marked (<c>Raw</c>) is re-read from its expanded body, so a <c>|</c> a value
            /// carries separates options here exactly as it does there. <c>null</c> when the node
            /// is not marked or the body does not change — then the caller counts the parsed tree,
            /// which is what the renderer walks in that case too.
            /// </summary>
            /// <remarks>
            /// What it can expand is what the census knows as TEXT: the row's values and
            /// <c>#set</c> macros. A <c>#def</c> is rolled once per render and its rolled text is
            /// unknowable here, so its reference stays literal in the body and keeps the symbolic
            /// <c>Poly.Ref</c> handling — a <c>#def</c> value carrying a <c>|</c> inside a
            /// construct is therefore still counted as one option (<c>docs/TODO.md</c>).
            /// </remarks>
            /// <summary>
            /// The body a marked construct was re-read from, by node — filled here and read by
            /// <see cref="BlankWaysOf"/>, which must ask the same tree this walk counted. Every
            /// node of the template is visited once per walk, and a definition or a value is
            /// parsed fresh at each visit, so an entry is never stale.
            /// </summary>
            private readonly Dictionary<Node, IReadOnlyList<Node>> _reReadNodes =
                new Dictionary<Node, IReadOnlyList<Node>>();

            private (Poly, long)? ReRead(Node owner, string? raw, char open, char close)
            {
                if (raw is null || _frozen) return null;
                // Only a row can take a branch; without one the conditional survives into the
                // re-parsed tree and both branches are counted, as everywhere else in this walk.
                var text = _forRow ? Renderer.ResolveConditionalsInText(raw, TakesThen) : raw;
                // The renderer's arithmetic: the fixpoint is 51 passes over the whole text, ONCE,
                // and a construct reached through a macro re-parse has already spent `_depth` of
                // those hops. A flat 51 here resolved a chain the render leaves literal.
                var expanded = ExpandMacros(text, PassesLeft);
                var body = _forRow ? Renderer.ResolveConditionalsInText(expanded.Text, TakesThen) : expanded.Text;
                if (body == raw) return null;
                var nodes = Parser.ParseSequence(open + body + close);
                _reReadNodes[owner] = nodes;
                if (expanded.Converged) return Guarded(() => Sequence(nodes), (Poly.One(), 0L));
                // Still changing on the last allowed pass: the renderer freezes the whole subtree,
                // so every reference left in it is literal text and earns no fresh allowance here
                // either — otherwise the count describes an expansion the render never performs.
                return Frozen(() => Guarded(() => Sequence(nodes), (Poly.One(), 0L)));
            }

            /// <summary>Run <paramref name="walk"/> with every remaining reference literal (the renderer's <c>WalkOptions.Frozen</c>).</summary>
            private T Frozen<T>(Func<T> walk)
            {
                var was = _frozen;
                _frozen = true;
                try { return walk(); }
                finally { _frozen = was; }
            }

            /// <summary>
            /// The renderer's <c>ExpandVarsFixpoint</c> over the names the census knows as text:
            /// the row's values first (they outrank everything), then <c>#set</c> macros; a
            /// <c>#def</c> name is left literal. Charged against <see cref="_spliceBudget"/> for
            /// the reason the renderer has a budget at all — <c>#set %a% = %b% %b%</c> over
            /// <c>#set %b% = %a% %a%</c> doubles every pass, and counting must not allocate what
            /// rendering refuses to.
            /// </summary>
            private Fixpoint ExpandMacros(string text, int passes)
            {
                var output = text;
                for (var i = 0; i < passes; i++)
                {
                    var changed = false;
                    output = VariableRe.Replace(output, m =>
                    {
                        var name = m.Groups[1].Value.ToLowerInvariant();
                        if (!_lowerVars.TryGetValue(name, out var value))
                        {
                            if (_ast.DefDefs.TryGetValue(name, out var def))
                            {
                                // A #def is rolled once and held. When its value carries no
                                // construct the roll is the value itself, so the text the renderer
                                // splices is known here too; when it carries one, the rolled text
                                // differs per render and the reference stays literal.
                                if (ContainsAnyOf(def, "{[%")) return m.Value;
                                value = def;
                            }
                            else if (!_ast.SetDefs.TryGetValue(name, out value)) return m.Value;
                        }
                        if (!Charge(value)) return m.Value;
                        changed = true;
                        return value;
                    });
                    if (!changed) return new Fixpoint { Text = output, Converged = true };
                }
                return new Fixpoint { Text = output, Converged = false };
            }

            /// <summary>The outcome of <see cref="ExpandMacros"/> — the renderer's <c>Fixpoint</c>, for the same caller decision.</summary>
            private struct Fixpoint
            {
                public string Text;
                public bool Converged;
            }

            /// <summary>
            /// Set, and has a non-whitespace char — the renderer's <c>is_truthy</c>. It reads the
            /// ROW only, while the renderer tests against the merged map, so a <c>#set</c> or a
            /// rolled <c>#def</c> that decides a branch is invisible here (<c>docs/TODO.md</c>).
            /// </summary>
            private bool TakesThen(string name, bool inverted)
            {
                var truthy = _lowerVars.TryGetValue(name.ToLowerInvariant(), out var value) && HasNonWhitespace(value);
                return inverted ? !truthy : truthy;
            }

            private (Poly, long) Variable(string rawName)
            {
                var name = rawName.ToLowerInvariant();
                // Inside a frozen subtree every reference is literal text, exactly as the renderer
                // emits it — not the value, and not the variety the value would have brought.
                if (_frozen) return (Poly.One(), ("%" + rawName + "%").Length);
                // Runtime context outranks a definition of the same name, as in the renderer —
                // and a value carrying constructs is re-parsed, as the renderer re-parses it.
                if (_lowerVars.TryGetValue(name, out var runtime))
                {
                    if (!Charge(runtime)) return Literal(rawName);
                    if (!ContainsAnyOf(runtime, "{[%")) return (Poly.One(), (long)runtime.Length);
                    return GuardedValue(() => Sequence(Parser.ParseSequence(runtime)), (Poly.One(), (long)runtime.Length));
                }
                if (_ast.DefDefs.ContainsKey(name))
                {
                    // The renderer substitutes the ROLLED text here and charges it, so charge the
                    // length this walk measured for it. That is the roll's length for a definition
                    // this walk can measure; a CYCLE is memoised to zero and charges nothing, which
                    // is one of the listed gaps (docs/TODO.md), not a bound.
                    var rolled = DefLength(name);
                    if (!Charge(rolled)) return Literal(rawName);
                    return (Poly.Ref(name), rolled);
                }
                if (_ast.SetDefs.TryGetValue(name, out var set))
                {
                    if (!Charge(set)) return Literal(rawName);
                    // A macro: re-rolled at every reference. At the cap the renderer returns the
                    // VALUE's text unexpanded (`ResolveVariable`), so that is the length here —
                    // zero said a chain ending at its cap contributes nothing, and `MaxLength`
                    // reported 2 for a render of 7.
                    return GuardedValue(() => Sequence(Parser.ParseSequence(set)), (Poly.One(), (long)set.Length));
                }
                // An unresolved name is emitted verbatim by the renderer, so it is that long.
                return Literal(rawName);
            }

            /// <summary>
            /// Debit one substitution against the expansion allowance, as the renderer does before
            /// every one of its own. <c>false</c> ⇒ the allowance is gone and the reference stays
            /// literal, which is both the renderer's behaviour and what stops a doubling macro from
            /// walking 2^50 nodes here.
            /// </summary>
            private bool Charge(string value) => Charge(value.Length);

            private bool Charge(long length)
            {
                if (_spliceBudget <= 0)
                {
                    _budgetExceeded = true;
                    return false;
                }
                _spliceBudget -= length;
                return true;
            }

            /// <summary>The reference as the renderer emits it when it does not expand.</summary>
            private (Poly, long) Literal(string rawName) => (Poly.One(), ("%" + rawName + "%").Length);

            /// <summary>
            /// A structural backstop for this walk's own recursion — definition evaluation and
            /// re-reads — NOT a semantic cap. It sits far above any real template because the
            /// renderer has no equivalent: a 55-long <c>#def</c> alias chain is rolled in full
            /// there, and a shared cap of 50 made <see cref="MaxLength"/> report 2 for a render
            /// of 3. The renderer's own cap is <see cref="MaxVarDepth"/>, on variable hops only.
            /// </summary>
            /// <remarks>
            /// 200, not 1000: this walk is recursive, and a 1100-long definition chain at 1000
            /// overflowed the stack (measured). Four times the renderer's variable cap covers any
            /// real document, and past it the answer SATURATES rather than understates, so the
            /// backstop costs precision on a template no one writes, never correctness. Raising it
            /// means making the definition traversal iterative first.
            /// </remarks>
            private const int MaxWalkDepth = 200;

            /// <summary>The renderer's <c>MAX_VARIABLE_DEPTH</c>: at the cap it returns the value's text unexpanded.</summary>
            private const int MaxVarDepth = 50;

            private T Guarded<T>(Func<T> walk, T atCap)
            {
                if (_depth >= MaxWalkDepth)
                {
                    // Abandoning a sub-tree makes every number downstream a guess, so the walk
                    // stops claiming one. The renderer has no such cap: it is a backstop here.
                    _capReached = true;
                    return atCap;
                }
                _depth++;
                try { return walk(); }
                finally { _depth--; }
            }

            /// <summary>
            /// <see cref="Guarded"/> for the two places that re-parse a VALUE — the one descent the
            /// renderer counts as a variable hop, and the one it caps.
            /// </summary>
            private T GuardedValue<T>(Func<T> walk, T atCap)
            {
                // The renderer's own cap, and it returns the value's text there — so this fallback
                // is what the render emits, not an abandoned sub-tree.
                if (_varDepth >= MaxVarDepth) return atCap;
                _varDepth++;
                try { return Guarded(walk, atCap); }
                finally { _varDepth--; }
            }

            /// <summary>
            /// The renderer's plural stage, mirrored: brackets in the forms → verbatim; arity →
            /// verbatim; for a row, the count resolved (row values, count conditionals) and
            /// tested as <c>-?[0-9]+</c> — non-numeric erases; a count that still depends on a
            /// <c>#def</c>/<c>#set</c> roll, or no row at all, counts every form.
            /// </summary>
            private (Poly, long) Plural(PluralNode p)
            {
                // Both slots take the same pass arithmetic as a re-read, and a frozen subtree
                // expands nothing — the renderer's fixpoint returns its text untouched there.
                var passes = PassesLeft;
                var countPass = _forRow && !_frozen ? ExpandRuntimeVars(p.CountRaw, passes) : new Fixpoint { Text = p.CountRaw, Converged = true };
                var formsPass = _forRow && !_frozen ? ExpandRuntimeVars(p.FormsRaw, passes) : new Fixpoint { Text = p.FormsRaw, Converged = true };
                var countRaw = countPass.Text;
                var formsRaw = formsPass.Text;
                long verbatim = ("{plural " + countRaw + ":" + formsRaw + "}").Length;
                if (ContainsAnyOf(formsRaw, "{}[]")) return (Poly.One(), verbatim);
                var forms = formsRaw.Split('|');
                for (var i = 0; i < forms.Length; i++) forms[i] = Parser.PhpTrim(forms[i]);
                if (forms.Length != Plurals.PluralArity(_baseLang)) return (Poly.One(), verbatim);

                if (_forRow)
                {
                    var count = ResolveCount(countRaw);
                    if (count != null)
                    {
                        count = Parser.PhpTrim(count);
                        if (!IntegerRe.IsMatch(count)) return (Poly.One(), 0);
                        var picked = Parser.ParseSequence(Plurals.PluralFor(_baseLang, ParseCount(count), forms));
                        // A form list whose passes ran out renders FROZEN, so what is left in the
                        // picked form is literal text — not a chain the count may follow further.
                        return formsPass.Converged ? Sequence(picked) : Frozen(() => Sequence(picked));
                    }
                }
                var poly = new Poly();
                long length = 0;
                foreach (var f in forms)
                {
                    var nodes = Parser.ParseSequence(f);
                    var (fp, l) = formsPass.Converged ? Sequence(nodes) : Frozen(() => Sequence(nodes));
                    poly = Poly.Add(poly, fp);
                    length = Math.Max(length, l);
                }
                return (poly, length);
            }

            /// <summary>%name% → the row's value, repeatedly, as the renderer's ExpandVarsFixpoint; other names stay.</summary>
            private Fixpoint ExpandRuntimeVars(string text, int passes)
            {
                var output = text;
                for (var i = 0; i < passes; i++)
                {
                    var changed = false;
                    output = VariableRe.Replace(output, m =>
                    {
                        if (!_lowerVars.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var value)) return m.Value;
                        // The same purse the renderer charges a plural slot against.
                        if (!Charge(value)) return m.Value;
                        changed = true;
                        return value;
                    });
                    if (!changed) return new Fixpoint { Text = output, Converged = true };
                }
                return new Fixpoint { Text = output, Converged = false };
            }

            /// <summary>
            /// The count slot as text after the renderer's textual passes: conditionals take
            /// their branch, literals and unresolved names stay. <c>null</c> when a <c>#def</c> or
            /// <c>#set</c> roll decides it. Any other construct makes it non-numeric.
            /// </summary>
            private string? ResolveCount(string countRaw)
            {
                if (!ContainsAnyOf(countRaw, "{%")) return countRaw;
                var sb = new System.Text.StringBuilder();
                return AppendCount(Parser.ParseSequence(countRaw), sb) ? sb.ToString() : null;
            }

            private bool AppendCount(IReadOnlyList<Node> nodes, System.Text.StringBuilder sb)
            {
                foreach (var n in nodes)
                {
                    switch (n)
                    {
                        case LiteralNode lit:
                            sb.Append(lit.Value);
                            break;
                        case VariableNode v:
                            {
                                var name = v.Name.ToLowerInvariant();
                                // Frozen, a directive reference is literal text — so the count is
                                // non-numeric and the render ERASES the block, rather than the
                                // "unknown roll, count every form" this returns otherwise.
                                if (!_frozen && (_ast.DefDefs.ContainsKey(name) || _ast.SetDefs.ContainsKey(name))) return false;
                                sb.Append('%').Append(v.Name).Append('%');
                                break;
                            }
                        case ConditionalNode c:
                            if (!AppendCount(TakesThen(c.Name, c.Inverted) ? c.Then : c.Else, sb)) return false;
                            break;
                        default:
                            sb.Append('{'); // an enumeration/permutation/plural here is never numeric
                            break;
                    }
                }
                return true;
            }

            /// <summary>JS parseInt of a digit run: exact to 2^53, then the nearest double, then ±Infinity.</summary>
            private static double ParseCount(string digits)
            {
                if (double.TryParse(digits, System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
                return digits.Length > 0 && digits[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            }

            /// <summary>
            /// The renderer's size range for a permutation of <paramref name="total"/> elements —
            /// the SURVIVING ones, which is what the renderer clamps against (spintax-js#80).
            /// </summary>
            private static (int min, int max) SizeRange(PermConfig cfg, int total)
            {
                int min, max;
                if (cfg.MinSize.HasValue && cfg.MaxSize.HasValue) { min = cfg.MinSize.Value; max = cfg.MaxSize.Value; }
                else if (cfg.MinSize.HasValue) { min = cfg.MinSize.Value; max = total; }
                else if (cfg.MaxSize.HasValue) { min = 1; max = cfg.MaxSize.Value; }
                else { min = total; max = total; }
                min = Math.Max(1, Math.Min(min, total));
                max = Math.Max(min, Math.Min(max, total));
                return (min, max);
            }

            /// <summary>
            /// The permutation, counted as the renderer draws it — the elements it DROPS included.
            /// An element whose rendered text is PHP-trim blank is no element (spintax-js#80), so
            /// the size clamp and the shuffle run over the survivors: <c>[a|{b|}|c]</c> draws 6
            /// ways when <c>{b|}</c> picks <c>b</c> and 2 when it picks nothing — 8, where the
            /// element count alone says 3!·e₃ = 12.
            /// </summary>
            /// <remarks>
            /// The usual e_k recurrence with a second dimension, <c>d</c>: how many elements have
            /// been dropped. Each element is dropped (its blank ways), chosen for the join (its
            /// non-blank ways), or a survivor the pick leaves out (ONE way — an element outside the
            /// pick has never multiplied anything here, because its text is discarded). Then every
            /// <c>d</c> is clamped on its own survivor count. With no droppable element — every
            /// ordinary template — the <c>d</c> dimension never grows and this is exactly the
            /// Σ k!·e_k it was, term for term.
            /// </remarks>
            private (Poly, long) Permutation(PermutationNode perm)
            {
                var n = perm.Options.Count;
                if (n == 0) return (Poly.One(), 0);
                var polys = new Poly[n];
                var blanks = new Poly[n];
                var fulls = new Poly[n];
                var lengths = new long[n];
                var droppable = 0;
                for (var i = 0; i < n; i++)
                {
                    var nodes = perm.Options[i].Nodes;
                    var (p, l) = Sequence(nodes);
                    polys[i] = p;
                    lengths[i] = l;
                    blanks[i] = Guarded(() => BlankWays(nodes), new Poly());
                    fulls[i] = Poly.Sub(p, blanks[i]);
                    if (!Poly.IsZero(blanks[i])) droppable++;
                }

                var cfg = perm.Config;

                // With no size written, the pick is every survivor, so a survivor the pick leaves
                // out cannot reach the answer at all — and skipping that transition keeps the walk
                // on one diagonal instead of filling the square. It is what a big permutation is:
                // a spliced list carries a `sep`, not a size.
                var everySurvivorIsPicked = !cfg.MinSize.HasValue && !cfg.MaxSize.HasValue;

                // dp[d][k]: the ways to have dropped d of the elements seen so far and chosen k of
                // the survivors. `d` never exceeds the number of elements that CAN come out blank,
                // and an element that can is not among the d already dropped when it is processed,
                // so droppable + 1 rows are enough. A null cell is zero — the layer is sparse, or
                // a 200-element permutation allocates 8 million of them.
                var dp = new Poly[droppable + 2][];
                AddTo(dp, 0, 0, Poly.One(), n);
                for (var i = 0; i < n; i++)
                {
                    var next = new Poly[droppable + 2][];
                    for (var d = 0; d < dp.Length; d++)
                    {
                        if (dp[d] == null) continue;
                        for (var k = 0; k < dp[d].Length; k++)
                        {
                            var cur = dp[d][k];
                            if (cur == null) continue;
                            if (!Poly.IsZero(blanks[i])) AddTo(next, d + 1, k, Poly.Mul(cur, blanks[i]), n);
                            if (!Poly.IsZero(fulls[i]))
                            {
                                AddTo(next, d, k + 1, Poly.Mul(cur, fulls[i]), n);
                                if (!everySurvivorIsPicked) AddTo(next, d, k, cur, n);
                            }
                        }
                    }
                    dp = next;
                }

                var total = new Poly();
                for (var d = 0; d < dp.Length; d++)
                {
                    if (dp[d] == null) continue;
                    var survivors = n - d;
                    // Every element blank: the renderer returns "" — one outcome, not none.
                    if (survivors == 0)
                    {
                        if (dp[d][0] != null) total = Poly.Add(total, dp[d][0]);
                        continue;
                    }
                    var (dMin, dMax) = SizeRange(cfg, survivors);
                    BigInteger fact = BigInteger.One;
                    for (var k = 1; k <= dMax; k++)
                    {
                        fact *= k;
                        if (k >= dMin && dp[d][k] != null) total = Poly.Add(total, Poly.Scale(dp[d][k], fact));
                    }
                }

                // The longest render drops nothing — dropping removes an element AND its separator,
                // and narrows the clamp with it — so the length is measured on the full element
                // list. It is an upper bound where an element can come out blank (docs/TODO.md).
                var (_, max) = SizeRange(cfg, n);

                // Longest: the `max` largest elements, joined as the renderer joins — sep between
                // the first ones, lastsep (or sep) once before the last, each padded as render
                // pads. Exact for global separators; with per-element separators the longest
                // of them stands in for every slot, an upper bound.
                Array.Sort(lengths);
                Array.Reverse(lengths);
                long body = 0;
                for (var i = 0; i < max; i++) body = SatAdd(body, lengths[i]);
                long sepLen = PaddedLength(cfg.Sep);
                long lastLen = PaddedLength(cfg.LastSep ?? cfg.Sep);
                foreach (var o in perm.Options)
                {
                    if (o.Separator is null) continue;
                    sepLen = Math.Max(sepLen, PaddedLength(o.Separator));
                    lastLen = Math.Max(lastLen, PaddedLength(o.Separator));
                }
                var joins = max < 2 ? 0 : SatAdd(SatMul(max - 2, sepLen), lastLen);
                return (total, SatAdd(body, joins));
            }

            /// <summary>Add a term into a sparse <c>dp[d][k]</c> layer of <see cref="Permutation"/> (null = zero).</summary>
            private static void AddTo(Poly[][] layer, int d, int k, Poly value, int n)
            {
                if (d >= layer.Length || k > n + 1) return;
                if (layer[d] == null) layer[d] = new Poly[n + 2];
                layer[d][k] = layer[d][k] == null ? value : Poly.Add(layer[d][k], value);
            }

            /// <summary>
            /// The ways a permutation element renders BLANK — PHP-trim whitespace only, which is
            /// what the renderer drops (spintax-js#80). An element is blank when every node in it
            /// is, so the walk is a product over the sequence and a sum over an enumeration's
            /// options, in the same term algebra as the count itself.
            /// </summary>
            /// <remarks>
            /// Structural, and deliberately answers "not blank" wherever it cannot prove blank:
            /// a variable (the renderer emits an undefined name verbatim, which is not blank, and
            /// a row value that is blank has already been spliced into the body by the re-read), a
            /// definition reference, a plural, and any construct whose counted tree is not its
            /// parsed tree (<c>Raw</c> — the re-read's body is). Those are gaps of the same family
            /// as the ones in <c>docs/TODO.md</c>, and each of them can only make this count higher
            /// than the render's, never lower than the tree's own.
            /// </remarks>
            private Poly BlankWays(IReadOnlyList<Node> nodes)
            {
                var poly = Poly.One();
                foreach (var node in nodes)
                {
                    var ways = BlankWaysOf(node);
                    if (Poly.IsZero(ways)) return new Poly(); // one node that never is ends it
                    poly = Poly.Mul(poly, ways);
                }
                return poly;
            }

            /// <summary>
            /// The blank ways of a marked construct: the body <see cref="ReRead"/> gave it, when
            /// this walk re-read one. No entry means the re-read changed nothing (an undefined
            /// name, a frozen subtree) and the tree was counted as parsed — but its own <c>Raw</c>
            /// says a value could still arrive, so the answer is "not blank".
            /// </summary>
            private Poly ReReadBlankWays(Node node) =>
                _reReadNodes.TryGetValue(node, out var nodes)
                    ? Guarded(() => BlankWays(nodes), new Poly())
                    : new Poly();

            private Poly BlankWaysOf(Node node)
            {
                switch (node)
                {
                    case LiteralNode lit:
                        return Parser.PhpTrim(lit.Value).Length == 0 ? Poly.One() : new Poly();
                    case EnumerationNode e:
                        {
                            // A marked construct is counted from the body the re-read gave it, so
                            // that is the tree to ask: `[a|{%v%|}|c]` with `v = x` re-reads to
                            // `{x|}`, whose empty option is what the renderer drops.
                            if (e.Raw != null) return ReReadBlankWays(e);
                            var poly = new Poly();
                            foreach (var option in e.Options)
                                poly = Poly.Add(poly, Guarded(() => BlankWays(option), new Poly()));
                            return poly;
                        }
                    case ConditionalNode c:
                        {
                            if (_forRow) return Guarded(() => BlankWays(TakesThen(c.Name, c.Inverted) ? c.Then : c.Else), new Poly());
                            return Poly.Add(
                                Guarded(() => BlankWays(c.Then), new Poly()),
                                Guarded(() => BlankWays(c.Else), new Poly()));
                        }
                    case PermutationNode perm:
                        {
                            // Blank exactly when every element of it is: then the renderer has no
                            // survivor left and returns "".
                            if (perm.Raw != null) return ReReadBlankWays(perm);
                            var poly = Poly.One();
                            foreach (var option in perm.Options)
                            {
                                var ways = Guarded(() => BlankWays(option.Nodes), new Poly());
                                if (Poly.IsZero(ways)) return new Poly();
                                poly = Poly.Mul(poly, ways);
                            }
                            return poly;
                        }
                    default:
                        return new Poly();
                }
            }

            private static long PaddedLength(string sep)
            {
                var trimmed = Parser.PhpTrim(sep);
                if (trimmed.Length == 0) return sep.Length;
                foreach (var ch in trimmed)
                    if (!char.IsLetter(ch)) return sep.Length;
                return trimmed.Length + 2; // the renderer pads a purely alphabetic separator
            }

            /// <summary>The renderer's truthiness test, character for character (<see cref="CharClass.IsUcpSpace"/>).</summary>
            private static bool HasNonWhitespace(string s)
            {
                foreach (var ch in s)
                    if (!CharClass.IsUcpSpace(ch)) return true;
                return false;
            }
        }
    }
}
