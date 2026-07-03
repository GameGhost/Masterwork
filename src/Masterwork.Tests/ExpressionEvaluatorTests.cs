using System;
using System.Collections.Generic;
using System.Linq;
using Masterwork.Engine;
using Xunit;

namespace Masterwork.Tests;

public class ExpressionEvaluatorTests
{
    // Deterministic fake: a fresh Random seeded from the seed key on every call, so repeated
    // calls with the same key reproduce the same value/order without needing the real SessionPrng.
    private sealed class FakeExprContext(Dictionary<string, ExprValue>? vars = null) : IExprContext
    {
        private readonly Dictionary<string, ExprValue> _vars = vars ?? [];

        public ExprValue GetVariable(string name) =>
            _vars.TryGetValue(name, out var v) ? v : throw new InvalidOperationException($"Unbound variable '{name}'");

        public long RandBetween(long min, long max, string seedKey)
        {
            var rng = new Random(seedKey.GetHashCode(StringComparison.Ordinal));
            return min + rng.Next((int)(max - min + 1));
        }

        public IReadOnlyList<ExprValue> Shuffled(IReadOnlyList<ExprValue> items, string seedKey)
        {
            var rng = new Random(seedKey.GetHashCode(StringComparison.Ordinal));
            var arr = items.ToList();
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }
    }

    private static ExprValue Eval(string expr, Dictionary<string, ExprValue>? vars = null) =>
        ExpressionEvaluator.Evaluate(expr, new FakeExprContext(vars));

    private static Dictionary<string, ExprValue> Vars(params (string name, ExprValue value)[] entries) =>
        entries.ToDictionary(e => e.name, e => e.value);

    // ── Basic ────────────────────────────────────────────────────────────────

    [Fact]
    public void IntLiteral_Evaluates() =>
        Assert.Equal(42L, Eval("42").AsInt());

    [Fact]
    public void StringLiteral_Evaluates() =>
        Assert.Equal("yes", Eval("\"yes\"").AsString());

    [Fact]
    public void BoolLiteral_Evaluates()
    {
        Assert.True(Eval("true").AsBool());
        Assert.False(Eval("false").AsBool());
    }

    [Fact]
    public void Addition_Evaluates() =>
        Assert.Equal(3L, Eval("round + 1", Vars(("round", ExprValue.Of(2L)))).AsInt());

    [Fact]
    public void Subtraction_Division_Modulo()
    {
        Assert.Equal(3L, Eval("10 / 3").AsInt());
        Assert.Equal(1L, Eval("10 % 3").AsInt());
        Assert.Equal(7L, Eval("10 - 3").AsInt());
    }

    [Fact]
    public void Comparison_LessThan() =>
        Assert.True(Eval("round < 4", Vars(("round", ExprValue.Of(3L)))).AsBool());

    [Fact]
    public void Equality_String() =>
        Assert.True(Eval("wolves == \"evil\"", Vars(("wolves", ExprValue.Of("evil")))).AsBool());

    [Fact]
    public void Equality_IntAsString_Coercion() =>
        Assert.True(Eval("bhome == 0", Vars(("bhome", ExprValue.Of("0")))).AsBool());

    [Fact]
    public void LogicalAnd_ShortCircuit() =>
        Assert.False(Eval("false && throw_if_evaluated").AsBool());

    [Fact]
    public void LogicalNot() =>
        Assert.True(Eval("!whpg", Vars(("whpg", ExprValue.Of(0L)))).AsBool());

    [Fact]
    public void ParseInt_OnStringVar() =>
        Assert.Equal(8L, Eval("parseInt(tracker) + 2", Vars(("tracker", ExprValue.Of("6")))).AsInt());

    [Fact]
    public void Max_Function() =>
        Assert.Equal(7L, Eval("max(scoreA, scoreB, scoreC)",
            Vars(("scoreA", ExprValue.Of(3L)), ("scoreB", ExprValue.Of(7L)), ("scoreC", ExprValue.Of(5L)))).AsInt());

    [Fact]
    public void RandBetween_IsInRange()
    {
        for (var i = 0; i < 50; i++)
        {
            var v = Eval($"rand_between(6, 10, \"k{i}\")").AsInt();
            Assert.InRange(v, 6, 10);
        }
    }

    [Fact]
    public void RandBetween_IsDeterministic()
    {
        var a = Eval("rand_between(1, 1000, \"stable_key\")").AsInt();
        var b = Eval("rand_between(1, 1000, \"stable_key\")").AsInt();
        Assert.Equal(a, b);
    }

    [Fact]
    public void ArrayLiteral_Evaluates()
    {
        var result = Eval("[nameA, nameB]", Vars(("nameA", ExprValue.Of("Alice")), ("nameB", ExprValue.Of("Bob")))).AsArray();
        Assert.Equal(["Alice", "Bob"], result.Select(v => v.AsString()));
    }

    [Fact]
    public void ArrayShuffled_IsDeterministic()
    {
        var vars = Vars(("nameA", ExprValue.Of("Alice")), ("nameB", ExprValue.Of("Bob")));
        var a = Eval("[nameA, nameB].shuffled(\"k\")", vars).AsArray().Select(v => v.AsString()).ToList();
        var b = Eval("[nameA, nameB].shuffled(\"k\")", vars).AsArray().Select(v => v.AsString()).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void ArrayCount() =>
        Assert.Equal(3L, Eval("elim.count()", Vars(("elim", ExprValue.Of(Arr("a", "b", "c"))))).AsInt());

    [Fact]
    public void ArrayExcept_Value()
    {
        var result = Eval("[1,2,3].except(2)").AsArray();
        Assert.Equal([1L, 3L], result.Select(v => v.AsInt()));
    }

    [Fact]
    public void ArrayCountif_Pattern() =>
        Assert.Equal(2L, Eval("arr.countif(\">3\")", Vars(("arr", ExprValue.Of(IntArr(1, 4, 5, 2))))).AsInt());

    [Fact]
    public void ArrayIndex_Zero() =>
        Assert.Equal("a", Eval("arr[0]", Vars(("arr", ExprValue.Of(Arr("a", "b", "c"))))).AsString());

    [Fact]
    public void ArrayIndex_LastCaret() =>
        Assert.Equal("c", Eval("arr[^1]", Vars(("arr", ExprValue.Of(Arr("a", "b", "c"))))).AsString());

    [Fact]
    public void DotPropertyAccess()
    {
        var entry = new ExprValue.RecordVal(new Dictionary<string, ExprValue> { ["points"] = ExprValue.Of(9L) });
        Assert.Equal(9L, Eval("entry.points", Vars(("entry", entry))).AsInt());
    }

    [Fact]
    public void RecordLiteral_Evaluates()
    {
        var result = Eval("{ player_name: nameA, points: scoreA }",
            Vars(("nameA", ExprValue.Of("Alice")), ("scoreA", ExprValue.Of(4L)))).AsRecord();
        Assert.Equal("Alice", result["player_name"].AsString());
        Assert.Equal(4L, result["points"].AsInt());
    }

    [Fact]
    public void OperatorPrecedence_MulBeforeAdd() =>
        Assert.Equal(14L, Eval("2 + 3 * 4").AsInt());

    [Fact]
    public void StringConcatenation() =>
        Assert.Equal("Hello Alice", Eval("\"Hello \" + nameA", Vars(("nameA", ExprValue.Of("Alice")))).AsString());

    [Fact]
    public void ArrayToSorted_Ascending()
    {
        var result = Eval("[3,1,2].toSorted(\"ascending\")").AsArray();
        Assert.Equal([1L, 2L, 3L], result.Select(v => v.AsInt()));
    }

    [Fact]
    public void ArrayToSorted_ByProperty()
    {
        ExprValue Player(string name, long points) => new ExprValue.RecordVal(new Dictionary<string, ExprValue>
        {
            ["player_name"] = ExprValue.Of(name),
            ["points"] = ExprValue.Of(points),
        });

        var playersRaw = ExprValue.Of(new List<ExprValue> { Player("A", 3), Player("B", 9), Player("C", 5) });
        var result = Eval("players_raw.toSorted(\"descending\", \"points\")", Vars(("players_raw", playersRaw))).AsArray();
        Assert.Equal(["B", "C", "A"], result.Select(v => v.AsRecord()["player_name"].AsString()));
    }

    // ── Complex / nested expressions ────────────────────────────────────────

    [Fact]
    public void Nested_ArrayMethodChain()
    {
        var vars = Vars(("nameA", ExprValue.Of("Alice")), ("nameB", ExprValue.Of("Bob")), ("nameC", ExprValue.Of("Cara")));
        var result = Eval("[nameA, nameB, nameC].shuffled(\"k\")[0]", vars).AsString();
        Assert.Contains(result, new[] { "Alice", "Bob", "Cara" });
    }

    [Fact]
    public void Nested_CountifOnFilteredArray()
    {
        var elim = ExprValue.Of(Arr("a", "dead", "b", "dead"));
        Assert.Equal(2L, Eval("elim.except(\"dead\").count()", Vars(("elim", elim))).AsInt());
    }

    [Fact]
    public void Nested_ConditionalInBoolExpr()
    {
        var vars = Vars(("round", ExprValue.Of(2L)), ("wolves", ExprValue.Of("evil")));
        Assert.True(Eval("(round > 1) && (wolves == \"evil\" || wolves == \"bad\")", vars).AsBool());
    }

    [Fact]
    public void Nested_ArithmeticInComparison()
    {
        var vars = Vars(
            ("scoreA", ExprValue.Of("3")), ("scoreB", ExprValue.Of("4")), ("scoreC", ExprValue.Of("2")));
        Assert.True(Eval("parseInt(scoreA) + parseInt(scoreB) > parseInt(scoreC) * 2", vars).AsBool());
    }

    [Fact]
    public void Nested_RecordInArray_IndexedAndAccessed()
    {
        var result = Eval("[{p: nameA, v: 1}][0].v + 1", Vars(("nameA", ExprValue.Of("Alice"))));
        Assert.Equal(2L, result.AsInt());
    }

    [Fact]
    public void Nested_FunctionInsideArithmetic()
    {
        var vars = Vars(
            ("scoreA", ExprValue.Of(5L)), ("scoreB", ExprValue.Of(7L)),
            ("scoreC", ExprValue.Of(1L)), ("scoreD", ExprValue.Of(3L)), ("round", ExprValue.Of(2L)));
        Assert.Equal(8L, Eval("max(scoreA, scoreB) - min(scoreC, scoreD) + round", vars).AsInt());
    }

    [Fact]
    public void Nested_SpreadInsideArray()
    {
        var elim = ExprValue.Of(Arr("a", "b"));
        var result = Eval("[..elim, \"new\"]", Vars(("elim", elim))).AsArray();
        Assert.Equal(["a", "b", "new"], result.Select(v => v.AsString()));
    }

    [Fact]
    public void Deep_TriplyNestedConditional()
    {
        var vars = Vars(
            ("a", ExprValue.Of(1L)), ("b", ExprValue.Of("notx")),
            ("c", ExprValue.Of("z")), ("d", ExprValue.Of(3L)));
        Assert.True(Eval("(a > 0) && ((b == \"x\") || (c != \"y\" && d < 5))", vars).AsBool());
    }

    [Fact]
    public void Expr_StringMethodsChained() =>
        Assert.True(Eval("nameA.toLower().contains(\"alice\")", Vars(("nameA", ExprValue.Of("ALICE")))).AsBool());

    [Fact]
    public void Expr_ParseIntInArrayCountif()
    {
        var scores = ExprValue.Of(IntArr(1, 3, 5, 2, 4));
        Assert.Equal(3L, Eval("scores.countif(\">= 3\")", Vars(("scores", scores))).AsInt());
    }

    private static List<ExprValue> Arr(params string[] values) => values.Select(ExprValue.Of).ToList();
    private static List<ExprValue> IntArr(params long[] values) => values.Select(ExprValue.Of).ToList();
}
