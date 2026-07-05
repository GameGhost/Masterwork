namespace Masterwork.Engine;

// Evaluates a parsed Expr AST against an IExprContext. Expressions are parsed once (see
// GetOrParse) and cached by source text, so repeated evaluation is a pure AST walk with no
// re-parsing cost.
public static class ExpressionEvaluator
{
    private static readonly Dictionary<string, Expr> Cache = new(StringComparer.Ordinal);
    private static readonly Lock CacheLock = new();

    public static Expr GetOrParse(string source)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(source, out var cached))
            {
                return cached;
            }

            var expr = ExpressionParser.Parse(source);
            Cache[source] = expr;
            return expr;
        }
    }

    public static ExprValue Evaluate(string source, IExprContext ctx) => Evaluate(GetOrParse(source), ctx);

    public static ExprValue Evaluate(Expr expr, IExprContext ctx) => expr switch
    {
        Expr.IntLiteral n => ExprValue.Of(n.Value),
        Expr.StringLiteral s => ExprValue.Of(s.Value),
        Expr.BoolLiteral b => ExprValue.Of(b.Value),
        Expr.VarRef v => ctx.GetVariable(v.Name),
        Expr.PropertyAccess p => EvalProperty(p, ctx),
        Expr.IndexAccess ix => EvalIndex(ix, ctx),
        Expr.Unary u => EvalUnary(u, ctx),
        Expr.Binary b => EvalBinary(b, ctx),
        Expr.MethodCall m => EvalMethod(m, ctx),
        Expr.FunctionCall f => EvalFunction(f, ctx),
        Expr.ArrayLiteral a => EvalArrayLiteral(a, ctx),
        Expr.RecordLiteral r => EvalRecordLiteral(r, ctx),
        _ => throw new ExprEvalException($"Unhandled expression node: {expr.GetType().Name}"),
    };

    private static ExprValue EvalProperty(Expr.PropertyAccess p, IExprContext ctx)
    {
        var target = Evaluate(p.Target, ctx);
        var record = target.AsRecord();
        if (!record.TryGetValue(p.Property, out var value))
        {
            throw new ExprEvalException($"Property '{p.Property}' not found on record");
        }

        return value;
    }

    // ── Indexing ─────────────────────────────────────────────────────────────

    private static ExprValue EvalIndex(Expr.IndexAccess ix, IExprContext ctx)
    {
        var target = Evaluate(ix.Target, ctx);
        return target switch
        {
            ExprValue.ArrayVal arr => EvalArrayIndex(arr.Items, ix.Arg, ctx),
            ExprValue.StringVal str => EvalStringIndex(str.Value, ix.Arg, ctx),
            _ => throw new ExprEvalException("Indexing requires an array or string target"),
        };
    }

    private static ExprValue EvalArrayIndex(IReadOnlyList<ExprValue> items, IndexArg arg, IExprContext ctx)
    {
        if (arg is IndexArg.Single single)
        {
            var idx = ResolveIndex(single, ctx, items.Count);
            if (idx < 0 || idx >= items.Count)
            {
                throw new ExprEvalException("Array index out of range");
            }

            return items[idx];
        }
        var (start, len) = ResolveRange((IndexArg.Range)arg, ctx, items.Count);
        return ExprValue.Of(items.Skip(start).Take(len).ToList());
    }

    private static ExprValue EvalStringIndex(string s, IndexArg arg, IExprContext ctx)
    {
        if (arg is IndexArg.Single single)
        {
            var idx = ResolveIndex(single, ctx, s.Length);
            if (idx < 0 || idx >= s.Length)
            {
                throw new ExprEvalException("String index out of range");
            }

            return ExprValue.Of(s[idx].ToString());
        }
        var (start, len) = ResolveRange((IndexArg.Range)arg, ctx, s.Length);
        return ExprValue.Of(s.Substring(start, len));
    }

    private static int ResolveIndex(IndexArg.Single single, IExprContext ctx, int length)
    {
        var raw = (int)Evaluate(single.Value, ctx).AsInt();
        return single.FromEnd ? length - raw : raw;
    }

    private static (int start, int len) ResolveRange(IndexArg.Range range, IExprContext ctx, int length)
    {
        int start = range.Start is null ? 0
            : range.StartFromEnd ? length - (int)Evaluate(range.Start, ctx).AsInt()
            : (int)Evaluate(range.Start, ctx).AsInt();
        int end = range.End is null ? length
            : range.EndFromEnd ? length - (int)Evaluate(range.End, ctx).AsInt()
            : (int)Evaluate(range.End, ctx).AsInt();
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, start, length);
        return (start, end - start);
    }

    // ── Operators ────────────────────────────────────────────────────────────

    private static ExprValue EvalUnary(Expr.Unary u, IExprContext ctx)
    {
        var v = Evaluate(u.Operand, ctx);
        return u.Op switch
        {
            "!" => ExprValue.Of(!v.AsBool()),
            "-" => ExprValue.Of(-v.AsInt()),
            _ => throw new ExprEvalException($"Unknown unary operator '{u.Op}'"),
        };
    }

    private static ExprValue EvalBinary(Expr.Binary b, IExprContext ctx)
    {
        // Logical operators short-circuit.
        if (b.Op == "&&")
        {
            return Evaluate(b.Left, ctx).AsBool() ? ExprValue.Of(Evaluate(b.Right, ctx).AsBool()) : ExprValue.Of(false);
        }

        if (b.Op == "||")
        {
            return Evaluate(b.Left, ctx).AsBool() ? ExprValue.Of(true) : ExprValue.Of(Evaluate(b.Right, ctx).AsBool());
        }

        var left = Evaluate(b.Left, ctx);
        var right = Evaluate(b.Right, ctx);

        return b.Op switch
        {
            "==" => ExprValue.Of(ExprValue.ValueEquals(left, right)),
            "!=" => ExprValue.Of(!ExprValue.ValueEquals(left, right)),
            "+" => EvalPlus(left, right),
            "-" => ExprValue.Of(left.AsInt() - right.AsInt()),
            "*" => ExprValue.Of(left.AsInt() * right.AsInt()),
            "/" => ExprValue.Of(left.AsInt() / right.AsInt()),
            "%" => ExprValue.Of(left.AsInt() % right.AsInt()),
            "<" => ExprValue.Of(left.AsInt() < right.AsInt()),
            "<=" => ExprValue.Of(left.AsInt() <= right.AsInt()),
            ">" => ExprValue.Of(left.AsInt() > right.AsInt()),
            ">=" => ExprValue.Of(left.AsInt() >= right.AsInt()),
            _ => throw new ExprEvalException($"Unknown binary operator '{b.Op}'"),
        };
    }

    // If either side is a string, `+` concatenates; otherwise it's integer addition. Variables are
    // typed per their VarDef declaration (VariableStore returns IntVal/StringVal accordingly), so
    // this dispatch matches the spec's "arithmetic on string var requires explicit parseInt()" rule.
    private static ExprValue EvalPlus(ExprValue left, ExprValue right) =>
        left is ExprValue.StringVal || right is ExprValue.StringVal
            ? ExprValue.Of(left.AsString() + right.AsString())
            : ExprValue.Of(left.AsInt() + right.AsInt());

    // ── Method calls (string / array operations) ────────────────────────────

    private static ExprValue EvalMethod(Expr.MethodCall m, IExprContext ctx)
    {
        var target = Evaluate(m.Target, ctx);
        var args = m.Args.Select(a => Evaluate(a, ctx)).ToList();

        return target switch
        {
            ExprValue.StringVal sv => EvalStringMethod(sv.Value, m.Method, args),
            ExprValue.ArrayVal av => EvalArrayMethod(av.Items, m.Method, args, ctx),
            _ => throw new ExprEvalException($"Method '{m.Method}' is not supported on this value"),
        };
    }

    private static ExprValue EvalStringMethod(string s, string method, List<ExprValue> args) => method switch
    {
        "length" => ExprValue.Of((long)s.Length),
        "contains" => ExprValue.Of(s.Contains(args[0].AsString(), StringComparison.Ordinal)),
        "toLower" => ExprValue.Of(s.ToLowerInvariant()),
        "toUpper" => ExprValue.Of(s.ToUpperInvariant()),
        "replace" => ExprValue.Of(s.Replace(args[0].AsString(), args[1].AsString(), StringComparison.Ordinal)),
        "substr" when args.Count == 1 => ExprValue.Of(s[(int)args[0].AsInt()..]),
        "substr" => ExprValue.Of(s[(int)args[0].AsInt()..(int)args[1].AsInt()]),
        _ => throw new ExprEvalException($"Unknown string method '{method}'"),
    };

    private static ExprValue EvalArrayMethod(IReadOnlyList<ExprValue> items, string method, List<ExprValue> args, IExprContext ctx) => method switch
    {
        "count" => ExprValue.Of((long)items.Count),
        "shuffled" => ExprValue.Of(ctx.Shuffled(items, args[0].AsString())),
        "toSorted" => EvalToSorted(items, args),
        "except" => EvalExcept(items, args[0]),
        "countif" => ExprValue.Of((long)items.Count(v => MatchesPattern(v, args[0].AsString()))),
        _ => throw new ExprEvalException($"Unknown array method '{method}'"),
    };

    private static ExprValue EvalToSorted(IReadOnlyList<ExprValue> items, List<ExprValue> args)
    {
        var dir = args[0].AsString();
        string? prop = args.Count > 1 ? args[1].AsString() : null;

        Comparison<ExprValue> cmp = prop is null
            ? CompareValues
            : (a, b) => CompareValues(a.AsRecord()[prop], b.AsRecord()[prop]);

        var sorted = items.ToList();
        sorted.Sort(cmp);
        if (dir == "descending")
        {
            sorted.Reverse();
        }

        return ExprValue.Of(sorted);
    }

    private static int CompareValues(ExprValue a, ExprValue b) =>
        a is ExprValue.IntVal || b is ExprValue.IntVal
            ? a.AsInt().CompareTo(b.AsInt())
            : string.CompareOrdinal(a.AsString(), b.AsString());

    private static ExprValue EvalExcept(IReadOnlyList<ExprValue> items, ExprValue arg)
    {
        if (arg is ExprValue.ArrayVal excludeArr)
        {
            return ExprValue.Of(items.Where(v => !excludeArr.Items.Any(e => ExprValue.ValueEquals(v, e))).ToList());
        }

        return ExprValue.Of(items.Where(v => !ExprValue.ValueEquals(v, arg)).ToList());
    }

    // Pattern strings for switch.cases[i].match and arr.countif(pattern):
    // bare value (equality), =value, >value, >=value, <value, <=value, !=value.
    // Property patterns for arrays of custom types: "property: pattern".
    // Internal: also used by PassageRenderer for switch.cases[i].match dispatch.
    internal static bool MatchesPattern(ExprValue value, string pattern)
    {
        var colonIdx = pattern.IndexOf(':');
        if (colonIdx > 0 && value is ExprValue.RecordVal rv)
        {
            var propName = pattern[..colonIdx].Trim();
            var subPattern = pattern[(colonIdx + 1)..].Trim();
            return rv.Properties.TryGetValue(propName, out var propVal) && MatchesPattern(propVal, subPattern);
        }

        var (op, operand) = ParsePatternOperator(pattern);
        return op switch
        {
            "=" or "" => MatchesEquality(value, operand),
            "!=" => !MatchesEquality(value, operand),
            ">" => value.AsInt() > long.Parse(operand),
            ">=" => value.AsInt() >= long.Parse(operand),
            "<" => value.AsInt() < long.Parse(operand),
            "<=" => value.AsInt() <= long.Parse(operand),
            _ => false,
        };
    }

    private static (string op, string operand) ParsePatternOperator(string pattern)
    {
        pattern = pattern.Trim();
        foreach (var op in new[] { ">=", "<=", "!=", "=", ">", "<" })
        {
            if (pattern.StartsWith(op, StringComparison.Ordinal))
            {
                return (op, pattern[op.Length..].Trim());
            }
        }

        return ("", pattern);
    }

    private static bool MatchesEquality(ExprValue value, string operand)
    {
        if (long.TryParse(operand, out var n))
        {
            return value.AsInt() == n;
        }

        var s = operand.Length >= 2 && operand[0] == '"' && operand[^1] == '"' ? operand[1..^1] : operand;
        return value.AsString() == s;
    }

    // ── Functions ────────────────────────────────────────────────────────────

    private static ExprValue EvalFunction(Expr.FunctionCall f, IExprContext ctx)
    {
        var args = f.Args.Select(a => Evaluate(a, ctx)).ToList();
        return f.Name switch
        {
            "rand_between" => ExprValue.Of(ctx.RandBetween(args[0].AsInt(), args[1].AsInt(), args[2].AsString())),
            "max" => ExprValue.Of(args.Select(a => a.AsInt()).Max()),
            "min" => ExprValue.Of(args.Select(a => a.AsInt()).Min()),
            "parseInt" => ExprValue.Of(args[0].AsInt()),
            _ => throw new ExprEvalException($"Unknown function '{f.Name}'"),
        };
    }

    // ── Literals ─────────────────────────────────────────────────────────────

    private static ExprValue EvalArrayLiteral(Expr.ArrayLiteral a, IExprContext ctx)
    {
        var items = new List<ExprValue>();
        foreach (var el in a.Elements)
        {
            if (el is ArrayElement.Spread sp)
            {
                items.AddRange(Evaluate(sp.Value, ctx).AsArray());
            }
            else if (el is ArrayElement.Item it)
            {
                items.Add(Evaluate(it.Value, ctx));
            }
        }
        return ExprValue.Of(items);
    }

    private static ExprValue EvalRecordLiteral(Expr.RecordLiteral r, IExprContext ctx)
    {
        var props = r.Properties.ToDictionary(kv => kv.Key, kv => Evaluate(kv.Value, ctx));
        return new ExprValue.RecordVal(props);
    }
}
