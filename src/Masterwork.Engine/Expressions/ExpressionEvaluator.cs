using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.Engine.Expressions;

/// <summary>
/// <inheritdoc cref="IExpressionEvaluator"/> Expressions are parsed once (see <see cref="GetOrParse"/>)
/// and cached by source text, so repeated evaluation is a pure AST walk with no re-parsing cost.
/// </summary>
public sealed class ExpressionEvaluator : IExpressionEvaluator
{
    private readonly IExpressionParser _parser;
    private readonly ILogger<ExpressionEvaluator> _logger;
    private readonly Dictionary<string, Expr> _cache = new(StringComparer.Ordinal);
    private readonly Lock _cacheLock = new();

    /// <summary>Creates an evaluator wired to the default <see cref="ExpressionParser"/>, discarding log output.</summary>
    public ExpressionEvaluator() : this(new ExpressionParser(), NullLogger<ExpressionEvaluator>.Instance)
    {
    }

    /// <summary>Creates an evaluator with an explicit parser dependency, e.g. for testing with mocks.</summary>
    public ExpressionEvaluator(IExpressionParser parser, ILogger<ExpressionEvaluator>? logger = null)
    {
        _parser = parser;
        _logger = logger ?? NullLogger<ExpressionEvaluator>.Instance;
    }

    /// <summary>Parses <paramref name="source"/> into an <see cref="Expr"/> AST, or returns the cached AST from a prior call with the same source text.</summary>
    public Expr GetOrParse(string source)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(source, out var cached))
            {
                return cached;
            }

            _logger.LogDebug("Expression cache miss, parsing: {Source}", source);
            var expr = _parser.Parse(source);
            _cache[source] = expr;
            return expr;
        }
    }

    /// <inheritdoc/>
    public StoryValue Evaluate(string source, IStoryEvalContext ctx) => Evaluate(GetOrParse(source), ctx);

    /// <inheritdoc/>
    public StoryValue Evaluate(Expr expr, IStoryEvalContext ctx) => expr switch
    {
        Expr.IntLiteral n => StoryValue.Of(n.Value),
        Expr.StringLiteral s => StoryValue.Of(s.Value),
        Expr.BoolLiteral b => StoryValue.Of(b.Value),
        Expr.VarRef v => ctx.GetVariable(v.Name),
        Expr.PropertyAccess p => EvalProperty(p, ctx),
        Expr.IndexAccess ix => EvalIndex(ix, ctx),
        Expr.Unary u => EvalUnary(u, ctx),
        Expr.Binary b => EvalBinary(b, ctx),
        Expr.Ternary t => Evaluate(t.Condition, ctx).AsBool() ? Evaluate(t.WhenTrue, ctx) : Evaluate(t.WhenFalse, ctx),
        Expr.MethodCall m => EvalMethod(m, ctx),
        Expr.FunctionCall f => EvalFunction(f, ctx),
        Expr.ArrayLiteral a => EvalArrayLiteral(a, ctx),
        Expr.RecordLiteral r => EvalRecordLiteral(r, ctx),
        _ => throw new StoryEvalException($"Unhandled expression node: {expr.GetType().Name}"),
    };

    private StoryValue EvalProperty(Expr.PropertyAccess p, IStoryEvalContext ctx)
    {
        var target = Evaluate(p.Target, ctx);
        var record = target.AsRecord();
        if (!record.TryGetValue(p.Property, out var value))
        {
            throw new StoryEvalException($"Property '{p.Property}' not found on record");
        }

        return value;
    }

    // ── Indexing ─────────────────────────────────────────────────────────────

    private StoryValue EvalIndex(Expr.IndexAccess ix, IStoryEvalContext ctx)
    {
        var target = Evaluate(ix.Target, ctx);
        return target switch
        {
            StoryValue.ArrayVal arr => EvalArrayIndex(arr.Items, ix.Arg, ctx),
            StoryValue.StringVal str => EvalStringIndex(str.Value, ix.Arg, ctx),
            _ => throw new StoryEvalException("Indexing requires an array or string target"),
        };
    }

    private StoryValue EvalArrayIndex(IReadOnlyList<StoryValue> items, IndexArg arg, IStoryEvalContext ctx)
    {
        if (arg is IndexArg.Single single)
        {
            var idx = ResolveIndex(single, ctx, items.Count);
            if (idx < 0 || idx >= items.Count)
            {
                throw new StoryEvalException("Array index out of range");
            }

            return items[idx];
        }
        var (start, len) = ResolveRange((IndexArg.Range)arg, ctx, items.Count);
        return StoryValue.Of(items.Skip(start).Take(len).ToList());
    }

    private StoryValue EvalStringIndex(string s, IndexArg arg, IStoryEvalContext ctx)
    {
        if (arg is IndexArg.Single single)
        {
            var idx = ResolveIndex(single, ctx, s.Length);
            if (idx < 0 || idx >= s.Length)
            {
                throw new StoryEvalException("String index out of range");
            }

            return StoryValue.Of(s[idx].ToString());
        }
        var (start, len) = ResolveRange((IndexArg.Range)arg, ctx, s.Length);
        return StoryValue.Of(s.Substring(start, len));
    }

    private int ResolveIndex(IndexArg.Single single, IStoryEvalContext ctx, int length)
    {
        var raw = (int)Evaluate(single.Value, ctx).AsInt();
        return single.FromEnd ? length - raw : raw;
    }

    private (int start, int len) ResolveRange(IndexArg.Range range, IStoryEvalContext ctx, int length)
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

    private StoryValue EvalUnary(Expr.Unary u, IStoryEvalContext ctx)
    {
        var v = Evaluate(u.Operand, ctx);
        return u.Op switch
        {
            "!" => StoryValue.Of(!v.AsBool()),
            "-" => StoryValue.Of(-v.AsInt()),
            _ => throw new StoryEvalException($"Unknown unary operator '{u.Op}'"),
        };
    }

    private StoryValue EvalBinary(Expr.Binary b, IStoryEvalContext ctx)
    {
        // Logical operators short-circuit.
        if (b.Op == "&&")
        {
            return Evaluate(b.Left, ctx).AsBool() ? StoryValue.Of(Evaluate(b.Right, ctx).AsBool()) : StoryValue.Of(false);
        }

        if (b.Op == "||")
        {
            return Evaluate(b.Left, ctx).AsBool() ? StoryValue.Of(true) : StoryValue.Of(Evaluate(b.Right, ctx).AsBool());
        }

        var left = Evaluate(b.Left, ctx);
        var right = Evaluate(b.Right, ctx);

        return b.Op switch
        {
            "==" => StoryValue.Of(StoryValue.ValueEquals(left, right)),
            "!=" => StoryValue.Of(!StoryValue.ValueEquals(left, right)),
            "+" => EvalPlus(left, right),
            "-" => StoryValue.Of(left.AsInt() - right.AsInt()),
            "*" => StoryValue.Of(left.AsInt() * right.AsInt()),
            "/" => StoryValue.Of(left.AsInt() / right.AsInt()),
            "%" => StoryValue.Of(left.AsInt() % right.AsInt()),
            "<" => StoryValue.Of(left.AsInt() < right.AsInt()),
            "<=" => StoryValue.Of(left.AsInt() <= right.AsInt()),
            ">" => StoryValue.Of(left.AsInt() > right.AsInt()),
            ">=" => StoryValue.Of(left.AsInt() >= right.AsInt()),
            _ => throw new StoryEvalException($"Unknown binary operator '{b.Op}'"),
        };
    }

    // If either side is a string, `+` concatenates; otherwise it's integer addition. Variables are
    // typed per their VarDef declaration (VariableStore returns IntVal/StringVal accordingly), so
    // this dispatch matches the spec's "arithmetic on string var requires explicit parseInt()" rule.
    private static StoryValue EvalPlus(StoryValue left, StoryValue right) =>
        left is StoryValue.StringVal || right is StoryValue.StringVal
            ? StoryValue.Of(left.AsString() + right.AsString())
            : StoryValue.Of(left.AsInt() + right.AsInt());

    // ── Method calls (string / array operations) ────────────────────────────

    private StoryValue EvalMethod(Expr.MethodCall m, IStoryEvalContext ctx)
    {
        var target = Evaluate(m.Target, ctx);
        var args = m.Args.Select(a => Evaluate(a, ctx)).ToList();

        return target switch
        {
            StoryValue.StringVal sv => EvalStringMethod(sv.Value, m.Method, args),
            StoryValue.ArrayVal av => EvalArrayMethod(av.Items, m.Method, args, ctx),
            _ => throw new StoryEvalException($"Method '{m.Method}' is not supported on this value"),
        };
    }

    private static StoryValue EvalStringMethod(string s, string method, List<StoryValue> args) => method switch
    {
        "length" => StoryValue.Of((long)s.Length),
        "contains" => StoryValue.Of(s.Contains(args[0].AsString(), StringComparison.Ordinal)),
        "toLower" => StoryValue.Of(s.ToLowerInvariant()),
        "toUpper" => StoryValue.Of(s.ToUpperInvariant()),
        "replace" => StoryValue.Of(s.Replace(args[0].AsString(), args[1].AsString(), StringComparison.Ordinal)),
        "substr" when args.Count == 1 => StoryValue.Of(s[(int)args[0].AsInt()..]),
        "substr" => StoryValue.Of(s[(int)args[0].AsInt()..(int)args[1].AsInt()]),
        _ => throw new StoryEvalException($"Unknown string method '{method}'"),
    };

    private StoryValue EvalArrayMethod(IReadOnlyList<StoryValue> items, string method, List<StoryValue> args, IStoryEvalContext ctx) => method switch
    {
        "count" => StoryValue.Of((long)items.Count),
        "shuffled" => StoryValue.Of(ctx.Shuffled(items, args[0].AsString())),
        "toSorted" => EvalToSorted(items, args),
        "except" => EvalExcept(items, args[0]),
        "countif" => StoryValue.Of((long)items.Count(v => MatchesPattern(v, args[0].AsString()))),
        _ => throw new StoryEvalException($"Unknown array method '{method}'"),
    };

    private static StoryValue EvalToSorted(IReadOnlyList<StoryValue> items, List<StoryValue> args)
    {
        var dir = args[0].AsString();
        string? prop = args.Count > 1 ? args[1].AsString() : null;

        Comparison<StoryValue> cmp = prop is null
            ? CompareValues
            : (a, b) => CompareValues(a.AsRecord()[prop], b.AsRecord()[prop]);

        var sorted = items.ToList();
        sorted.Sort(cmp);
        if (dir == "descending")
        {
            sorted.Reverse();
        }

        return StoryValue.Of(sorted);
    }

    private static int CompareValues(StoryValue a, StoryValue b) =>
        a is StoryValue.IntVal || b is StoryValue.IntVal
            ? a.AsInt().CompareTo(b.AsInt())
            : string.CompareOrdinal(a.AsString(), b.AsString());

    private static StoryValue EvalExcept(IReadOnlyList<StoryValue> items, StoryValue arg)
    {
        if (arg is StoryValue.ArrayVal excludeArr)
        {
            return StoryValue.Of(items.Where(v => !excludeArr.Items.Any(e => StoryValue.ValueEquals(v, e))).ToList());
        }

        return StoryValue.Of(items.Where(v => !StoryValue.ValueEquals(v, arg)).ToList());
    }

    /// <inheritdoc/>
    public bool MatchesPattern(StoryValue value, string pattern)
    {
        var colonIdx = pattern.IndexOf(':');
        if (colonIdx > 0 && value is StoryValue.RecordVal rv)
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

    private static bool MatchesEquality(StoryValue value, string operand)
    {
        if (long.TryParse(operand, out var n))
        {
            return value.AsInt() == n;
        }

        var s = operand.Length >= 2 && operand[0] == '"' && operand[^1] == '"' ? operand[1..^1] : operand;
        return value.AsString() == s;
    }

    // ── Functions ────────────────────────────────────────────────────────────

    private StoryValue EvalFunction(Expr.FunctionCall f, IStoryEvalContext ctx)
    {
        var args = f.Args.Select(a => Evaluate(a, ctx)).ToList();
        return f.Name switch
        {
            "rand_between" => StoryValue.Of(ctx.RandBetween(args[0].AsInt(), args[1].AsInt(), args[2].AsString())),
            "max" => StoryValue.Of(args.Select(a => a.AsInt()).Max()),
            "min" => StoryValue.Of(args.Select(a => a.AsInt()).Min()),
            "parseInt" => StoryValue.Of(args[0].AsInt()),
            _ => throw new StoryEvalException($"Unknown function '{f.Name}'"),
        };
    }

    // ── Literals ─────────────────────────────────────────────────────────────

    private StoryValue EvalArrayLiteral(Expr.ArrayLiteral a, IStoryEvalContext ctx)
    {
        var items = new List<StoryValue>();
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
        return StoryValue.Of(items);
    }

    private StoryValue EvalRecordLiteral(Expr.RecordLiteral r, IStoryEvalContext ctx)
    {
        var props = r.Properties.ToDictionary(kv => kv.Key, kv => Evaluate(kv.Value, ctx));
        return new StoryValue.RecordVal(props);
    }
}
