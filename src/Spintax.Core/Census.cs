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
    /// <c>k!·e_k(c₁…cₙ)</c>. Plurals follow the renderer's stage: brackets in the forms or the
    /// wrong arity emit the construct verbatim (one outcome); for a row, a non-numeric count
    /// erases the block and a count that depends on a <c>#def</c> roll counts every form. Values
    /// from the row that contain constructs are re-parsed, as the renderer re-parses them. Counts
    /// and lengths saturate at <see cref="long.MaxValue"/>. The longest permutation is exact
    /// for global separators; with per-element separators the longest of them stands in for every
    /// slot, an upper bound.
    /// </remarks>
    internal static class Census
    {
        public static long Combinations(ParsedAst ast, IReadOnlyDictionary<string, string>? vars, string? locale)
        {
            var w = new Walker(ast, vars, locale);
            var total = w.Expand(w.Sequence(ast.Nodes).poly);
            return total > long.MaxValue ? long.MaxValue : (long)total;
        }

        public static long MaxLength(ParsedAst ast, IReadOnlyDictionary<string, string>? vars, string? locale)
        {
            var w = new Walker(ast, vars, locale);
            return w.Sequence(ast.Nodes).length;
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
        }

        private sealed class Walker
        {
            private readonly ParsedAst _ast;
            private readonly Dictionary<string, string> _lowerVars = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly bool _forRow;
            private readonly string _baseLang;
            private readonly Dictionary<string, Poly> _defPolys = new Dictionary<string, Poly>(StringComparer.Ordinal);
            private readonly Dictionary<string, long> _defLengths = new Dictionary<string, long>(StringComparer.Ordinal);
            private int _depth;

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
                        return Permutation(perm);
                    default:
                        return (Poly.One(), 0);
                }
            }

            /// <summary>Truthy exactly as the renderer: set, and has a non-whitespace char.</summary>
            private bool TakesThen(string name, bool inverted)
            {
                var truthy = _lowerVars.TryGetValue(name.ToLowerInvariant(), out var value) && HasNonWhitespace(value);
                return inverted ? !truthy : truthy;
            }

            private (Poly, long) Variable(string rawName)
            {
                var name = rawName.ToLowerInvariant();
                // Runtime context outranks a definition of the same name, as in the renderer —
                // and a value carrying constructs is re-parsed, as the renderer re-parses it.
                if (_lowerVars.TryGetValue(name, out var runtime))
                {
                    if (!ContainsAnyOf(runtime, "{[%")) return (Poly.One(), runtime.Length);
                    return Guarded(() => Sequence(Parser.ParseSequence(runtime)), (Poly.One(), (long)runtime.Length));
                }
                if (_ast.DefDefs.ContainsKey(name)) return (Poly.Ref(name), DefLength(name));
                if (_ast.SetDefs.TryGetValue(name, out var set))
                    return Guarded(() => Sequence(Parser.ParseSequence(set)), (Poly.One(), 0L)); // a macro: re-rolled here
                return (Poly.One(), _forRow ? 0 : ("%" + rawName + "%").Length);
            }

            /// <summary>A self-referencing or very deep chain ends at the renderer's depth cap; so does the count.</summary>
            private T Guarded<T>(Func<T> walk, T atCap)
            {
                if (_depth >= 50) return atCap;
                _depth++;
                try { return walk(); }
                finally { _depth--; }
            }

            /// <summary>
            /// The renderer's plural stage, mirrored: brackets in the forms → verbatim; arity →
            /// verbatim; for a row, the count resolved (row values, count conditionals) and
            /// tested as <c>-?[0-9]+</c> — non-numeric erases; a count that still depends on a
            /// <c>#def</c>/<c>#set</c> roll, or no row at all, counts every form.
            /// </summary>
            private (Poly, long) Plural(PluralNode p)
            {
                var countRaw = _forRow ? ExpandRuntimeVars(p.CountRaw) : p.CountRaw;
                var formsRaw = _forRow ? ExpandRuntimeVars(p.FormsRaw) : p.FormsRaw;
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
                        return Sequence(Parser.ParseSequence(Plurals.PluralFor(_baseLang, ParseCount(count), forms)));
                    }
                }
                var poly = new Poly();
                long length = 0;
                foreach (var f in forms)
                {
                    var (fp, l) = Sequence(Parser.ParseSequence(f));
                    poly = Poly.Add(poly, fp);
                    length = Math.Max(length, l);
                }
                return (poly, length);
            }

            /// <summary>%name% → the row's value, repeatedly, as the renderer's ExpandVarsOnly; other names stay.</summary>
            private string ExpandRuntimeVars(string text)
            {
                var output = text;
                for (var i = 0; i < 50; i++)
                {
                    var changed = false;
                    output = VariableRe.Replace(output, m =>
                    {
                        if (!_lowerVars.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var value)) return m.Value;
                        changed = true;
                        return value;
                    });
                    if (!changed) break;
                }
                return output;
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
                                if (_ast.DefDefs.ContainsKey(name) || _ast.SetDefs.ContainsKey(name)) return false;
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

            private (Poly, long) Permutation(PermutationNode perm)
            {
                var n = perm.Options.Count;
                if (n == 0) return (Poly.One(), 0);
                var polys = new Poly[n];
                var lengths = new long[n];
                for (var i = 0; i < n; i++)
                {
                    var (p, l) = Sequence(perm.Options[i].Nodes);
                    polys[i] = p;
                    lengths[i] = l;
                }

                // The size range exactly as the renderer resolves it.
                int min, max;
                var cfg = perm.Config;
                if (cfg.MinSize.HasValue && cfg.MaxSize.HasValue) { min = cfg.MinSize.Value; max = cfg.MaxSize.Value; }
                else if (cfg.MinSize.HasValue) { min = cfg.MinSize.Value; max = n; }
                else if (cfg.MaxSize.HasValue) { min = 1; max = cfg.MaxSize.Value; }
                else { min = n; max = n; }
                min = Math.Max(1, Math.Min(min, n));
                max = Math.Max(min, Math.Min(max, n));

                // e_k over polynomials by the standard recurrence; combinations = Σ_k k!·e_k.
                var e = new Poly[n + 1];
                e[0] = Poly.One();
                for (var k = 1; k <= n; k++) e[k] = new Poly();
                foreach (var p in polys)
                    for (var k = n; k >= 1; k--) e[k] = Poly.Add(e[k], Poly.Mul(e[k - 1], p));
                var total = new Poly();
                BigInteger factorial = BigInteger.One;
                for (var k = 1; k <= max; k++)
                {
                    factorial *= k;
                    if (k >= min) total = Poly.Add(total, Poly.Scale(e[k], factorial));
                }

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

            private static long PaddedLength(string sep)
            {
                var trimmed = Parser.PhpTrim(sep);
                if (trimmed.Length == 0) return sep.Length;
                foreach (var ch in trimmed)
                    if (!char.IsLetter(ch)) return sep.Length;
                return trimmed.Length + 2; // the renderer pads a purely alphabetic separator
            }

            private static bool HasNonWhitespace(string s)
            {
                foreach (var ch in s)
                    if (!JsText.IsJsWhiteSpace(ch)) return true;
                return false;
            }
        }
    }
}
