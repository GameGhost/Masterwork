using Masterwork.Engine;
using Masterwork.Engine.Expressions;

namespace Masterwork.Tests;

public class ExpressionEvaluatorTests
{
    // Deterministic fake: a fresh Random seeded from the seed key on every call, so repeated
    // calls with the same key reproduce the same value/order without needing the real SessionPrng.
    private sealed class FakeExprContext(Dictionary<string, StoryValue>? vars = null) : IStoryEvalContext
    {
        private readonly Dictionary<string, StoryValue> _vars = vars ?? [];

        public StoryValue GetVariable(string name) =>
            _vars.TryGetValue(name, out var v) ? v : throw new InvalidOperationException($"Unbound variable '{name}'");

        public long RandBetween(long min, long max, string seedKey)
        {
            var rng = new Random(seedKey.GetHashCode(StringComparison.Ordinal));
            return min + rng.Next((int)(max - min + 1));
        }

        public IReadOnlyList<StoryValue> Shuffled(IReadOnlyList<StoryValue> items, string seedKey)
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

    private static StoryValue Eval(string expr, Dictionary<string, StoryValue>? vars = null) =>
        new ExpressionEvaluator().Evaluate(expr, new FakeExprContext(vars));

    private static Dictionary<string, StoryValue> Vars(params (string name, StoryValue value)[] entries) =>
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
        Assert.Equal(3L, Eval("round + 1", Vars(("round", StoryValue.Of(2L)))).AsInt());

    [Fact]
    public void Subtraction_Division_Modulo()
    {
        Assert.Equal(3L, Eval("10 / 3").AsInt());
        Assert.Equal(1L, Eval("10 % 3").AsInt());
        Assert.Equal(7L, Eval("10 - 3").AsInt());
    }

    [Fact]
    public void Comparison_LessThan() =>
        Assert.True(Eval("round < 4", Vars(("round", StoryValue.Of(3L)))).AsBool());

    [Fact]
    public void Equality_String() =>
        Assert.True(Eval("wolves == \"evil\"", Vars(("wolves", StoryValue.Of("evil")))).AsBool());

    [Fact]
    public void Equality_IntAsString_Coercion() =>
        Assert.True(Eval("bhome == 0", Vars(("bhome", StoryValue.Of("0")))).AsBool());

    [Fact]
    public void LogicalAnd_ShortCircuit() =>
        Assert.False(Eval("false && throw_if_evaluated").AsBool());

    [Fact]
    public void LogicalNot() =>
        Assert.True(Eval("!whpg", Vars(("whpg", StoryValue.Of(0L)))).AsBool());

    [Fact]
    public void ParseInt_OnStringVar() =>
        Assert.Equal(8L, Eval("parseInt(tracker) + 2", Vars(("tracker", StoryValue.Of("6")))).AsInt());

    [Fact]
    public void Max_Function() =>
        Assert.Equal(7L, Eval("max(scoreA, scoreB, scoreC)",
            Vars(("scoreA", StoryValue.Of(3L)), ("scoreB", StoryValue.Of(7L)), ("scoreC", StoryValue.Of(5L)))).AsInt());

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
        var result = Eval("[nameA, nameB]", Vars(("nameA", StoryValue.Of("Alice")), ("nameB", StoryValue.Of("Bob")))).AsArray();
        Assert.Equal(["Alice", "Bob"], result.Select(v => v.AsString()));
    }

    [Fact]
    public void ArrayShuffled_IsDeterministic()
    {
        var vars = Vars(("nameA", StoryValue.Of("Alice")), ("nameB", StoryValue.Of("Bob")));
        var a = Eval("[nameA, nameB].shuffled(\"k\")", vars).AsArray().Select(v => v.AsString()).ToList();
        var b = Eval("[nameA, nameB].shuffled(\"k\")", vars).AsArray().Select(v => v.AsString()).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void ArrayCount() =>
        Assert.Equal(3L, Eval("elim.count()", Vars(("elim", StoryValue.Of(Arr("a", "b", "c"))))).AsInt());

    [Fact]
    public void ArrayExcept_Value()
    {
        var result = Eval("[1,2,3].except(2)").AsArray();
        Assert.Equal([1L, 3L], result.Select(v => v.AsInt()));
    }

    [Fact]
    public void ArrayCountif_Pattern() =>
        Assert.Equal(2L, Eval("arr.countif(\">3\")", Vars(("arr", StoryValue.Of(IntArr(1, 4, 5, 2))))).AsInt());

    [Fact]
    public void ArrayIndex_Zero() =>
        Assert.Equal("a", Eval("arr[0]", Vars(("arr", StoryValue.Of(Arr("a", "b", "c"))))).AsString());

    [Fact]
    public void ArrayIndex_LastCaret() =>
        Assert.Equal("c", Eval("arr[^1]", Vars(("arr", StoryValue.Of(Arr("a", "b", "c"))))).AsString());

    [Fact]
    public void DotPropertyAccess()
    {
        var entry = new StoryValue.RecordVal(new Dictionary<string, StoryValue> { ["points"] = StoryValue.Of(9L) });
        Assert.Equal(9L, Eval("entry.points", Vars(("entry", entry))).AsInt());
    }

    [Fact]
    public void RecordLiteral_Evaluates()
    {
        var result = Eval("{ player_name: nameA, points: scoreA }",
            Vars(("nameA", StoryValue.Of("Alice")), ("scoreA", StoryValue.Of(4L)))).AsRecord();
        Assert.Equal("Alice", result["player_name"].AsString());
        Assert.Equal(4L, result["points"].AsInt());
    }

    [Fact]
    public void OperatorPrecedence_MulBeforeAdd() =>
        Assert.Equal(14L, Eval("2 + 3 * 4").AsInt());

    [Fact]
    public void StringConcatenation() =>
        Assert.Equal("Hello Alice", Eval("\"Hello \" + nameA", Vars(("nameA", StoryValue.Of("Alice")))).AsString());

    [Fact]
    public void ArrayToSorted_Ascending()
    {
        var result = Eval("[3,1,2].toSorted(\"ascending\")").AsArray();
        Assert.Equal([1L, 2L, 3L], result.Select(v => v.AsInt()));
    }

    [Fact]
    public void ArrayToSorted_ByProperty()
    {
        StoryValue Player(string name, long points) => new StoryValue.RecordVal(new Dictionary<string, StoryValue>
        {
            ["player_name"] = StoryValue.Of(name),
            ["points"] = StoryValue.Of(points),
        });

        var playersRaw = StoryValue.Of(new List<StoryValue> { Player("A", 3), Player("B", 9), Player("C", 5) });
        var result = Eval("players_raw.toSorted(\"descending\", \"points\")", Vars(("players_raw", playersRaw))).AsArray();
        Assert.Equal(["B", "C", "A"], result.Select(v => v.AsRecord()["player_name"].AsString()));
    }

    // ── Ternary conditional ──────────────────────────────────────────────────

    [Fact]
    public void Ternary_TrueBranch() =>
        Assert.Equal("yes", Eval("round == 1 ? \"yes\" : \"no\"", Vars(("round", StoryValue.Of(1L)))).AsString());

    [Fact]
    public void Ternary_FalseBranch() =>
        Assert.Equal("no", Eval("round == 1 ? \"yes\" : \"no\"", Vars(("round", StoryValue.Of(2L)))).AsString());

    [Fact]
    public void Ternary_ShortCircuits_OnlyEvaluatesTakenBranch() =>
        Assert.Equal("yes", Eval("true ? \"yes\" : throw_if_evaluated").AsString());

    // Reproduces the exact shape extracted from Cost of Disease's computed popup targets
    // (e.g. Gen1CreepyYes.mws.yaml) — a right-associative chain reading as if/else-if.
    [Fact]
    public void Ternary_ChainedRightAssociative_PicksFirstMatch()
    {
        var expr = "round == 1 ? \"Fever1\" : round == 2 ? \"Fever2\" : \"Fever3\"";
        Assert.Equal("Fever1", Eval(expr, Vars(("round", StoryValue.Of(1L)))).AsString());
        Assert.Equal("Fever2", Eval(expr, Vars(("round", StoryValue.Of(2L)))).AsString());
        Assert.Equal("Fever3", Eval(expr, Vars(("round", StoryValue.Of(3L)))).AsString());
    }

    [Fact]
    public void Ternary_InsideParentheses() =>
        Assert.Equal(6L, Eval("(true ? 1 : 2) + (false ? 3 : 5)").AsInt());

    [Fact]
    public void Ternary_AsFunctionArgument() =>
        Assert.Equal(5L, Eval("max(round == 1 ? 5 : 1, 2)", Vars(("round", StoryValue.Of(1L)))).AsInt());

    // ── Complex / nested expressions ────────────────────────────────────────

    [Fact]
    public void Nested_ArrayMethodChain()
    {
        var vars = Vars(("nameA", StoryValue.Of("Alice")), ("nameB", StoryValue.Of("Bob")), ("nameC", StoryValue.Of("Cara")));
        var result = Eval("[nameA, nameB, nameC].shuffled(\"k\")[0]", vars).AsString();
        Assert.Contains(result, new[] { "Alice", "Bob", "Cara" });
    }

    [Fact]
    public void Nested_CountifOnFilteredArray()
    {
        var elim = StoryValue.Of(Arr("a", "dead", "b", "dead"));
        Assert.Equal(2L, Eval("elim.except(\"dead\").count()", Vars(("elim", elim))).AsInt());
    }

    [Fact]
    public void Nested_ConditionalInBoolExpr()
    {
        var vars = Vars(("round", StoryValue.Of(2L)), ("wolves", StoryValue.Of("evil")));
        Assert.True(Eval("(round > 1) && (wolves == \"evil\" || wolves == \"bad\")", vars).AsBool());
    }

    [Fact]
    public void Nested_ArithmeticInComparison()
    {
        var vars = Vars(
            ("scoreA", StoryValue.Of("3")), ("scoreB", StoryValue.Of("4")), ("scoreC", StoryValue.Of("2")));
        Assert.True(Eval("parseInt(scoreA) + parseInt(scoreB) > parseInt(scoreC) * 2", vars).AsBool());
    }

    [Fact]
    public void Nested_RecordInArray_IndexedAndAccessed()
    {
        var result = Eval("[{p: nameA, v: 1}][0].v + 1", Vars(("nameA", StoryValue.Of("Alice"))));
        Assert.Equal(2L, result.AsInt());
    }

    [Fact]
    public void Nested_FunctionInsideArithmetic()
    {
        var vars = Vars(
            ("scoreA", StoryValue.Of(5L)), ("scoreB", StoryValue.Of(7L)),
            ("scoreC", StoryValue.Of(1L)), ("scoreD", StoryValue.Of(3L)), ("round", StoryValue.Of(2L)));
        Assert.Equal(8L, Eval("max(scoreA, scoreB) - min(scoreC, scoreD) + round", vars).AsInt());
    }

    [Fact]
    public void Nested_SpreadInsideArray()
    {
        var elim = StoryValue.Of(Arr("a", "b"));
        var result = Eval("[..elim, \"new\"]", Vars(("elim", elim))).AsArray();
        Assert.Equal(["a", "b", "new"], result.Select(v => v.AsString()));
    }

    [Fact]
    public void Deep_TriplyNestedConditional()
    {
        var vars = Vars(
            ("a", StoryValue.Of(1L)), ("b", StoryValue.Of("notx")),
            ("c", StoryValue.Of("z")), ("d", StoryValue.Of(3L)));
        Assert.True(Eval("(a > 0) && ((b == \"x\") || (c != \"y\" && d < 5))", vars).AsBool());
    }

    [Fact]
    public void Expr_StringMethodsChained() =>
        Assert.True(Eval("nameA.toLower().contains(\"alice\")", Vars(("nameA", StoryValue.Of("ALICE")))).AsBool());

    [Fact]
    public void Expr_ParseIntInArrayCountif()
    {
        var scores = StoryValue.Of(IntArr(1, 3, 5, 2, 4));
        Assert.Equal(3L, Eval("scores.countif(\">= 3\")", Vars(("scores", scores))).AsInt());
    }

    // ── String literal interpolation ────────────────────────────────────────
    // Quoted string literals support the same {expr} placeholder syntax as display-text templates
    // (VariableStore.ExpandTemplate) — a combining assign like newspaper = "The {townname} {name}"
    // evaluates the whole sentence in one place instead of needing a hand-built '+' chain.

    [Fact]
    public void StringLiteral_NoBraces_IsLiteral() =>
        Assert.Equal("plain text", Eval("\"plain text\"").AsString());

    [Fact]
    public void StringLiteral_SingleVarPlaceholder_Interpolates() =>
        Assert.Equal("Riverside", Eval("\"{townname}\"", Vars(("townname", StoryValue.Of("Riverside")))).AsString());

    [Fact]
    public void StringLiteral_MixedTextAndMultiplePlaceholders_Interpolates() =>
        Assert.Equal(
            "The Riverside Ledger",
            Eval("\"The {townname} {name}\"", Vars(("townname", StoryValue.Of("Riverside")), ("name", StoryValue.Of("Ledger")))).AsString());

    [Fact]
    public void StringLiteral_PlaceholderIsFullExpression_NotJustBareVar() =>
        // ExpandTemplate evaluates the whole {…} content as an expression, not just a variable
        // lookup — matches VariableStoreTests' equivalent coverage for display-text templates
        // (e.g. {elim[0]}, {entry.player_name}).
        Assert.Equal("6", Eval("\"{a + b}\"", Vars(("a", StoryValue.Of(2L)), ("b", StoryValue.Of(4L)))).AsString());

    [Fact]
    public void StringLiteral_IconPlaceholder_PassesThroughUnexpanded() =>
        // {icon:slug} is resolved later at text-render time, not by the expression evaluator —
        // same as VariableStoreTests.ExpandTemplate_IconPlaceholder_PassesThrough for display text.
        Assert.Equal("{icon:angrymob_icon}", Eval("\"{icon:angrymob_icon}\"").AsString());

    [Fact]
    public void StringLiteral_Interpolation_ReflectsCurrentContextEachEvaluation()
    {
        // Same cached AST, different ctx each call — interpolation must re-evaluate against the
        // CURRENT variable value, not bake in whatever was live at parse/first-eval time.
        var evaluator = new ExpressionEvaluator();
        var expr = evaluator.GetOrParse("\"Hello {name}\"");
        Assert.Equal("Hello Alice", evaluator.Evaluate(expr, new FakeExprContext(Vars(("name", StoryValue.Of("Alice"))))).AsString());
        Assert.Equal("Hello Bob", evaluator.Evaluate(expr, new FakeExprContext(Vars(("name", StoryValue.Of("Bob"))))).AsString());
    }

    [Fact]
    public void StringLiteral_InsideConcatenation_Interpolates() =>
        // The exact "newspaper" shape: a mixed-template quoted literal combined via '+' with a
        // separately-computed value (e.g. a random-chosen name fragment).
        Assert.Equal(
            "The Riverside Gazette",
            Eval("\"The {townname} \" + name", Vars(("townname", StoryValue.Of("Riverside")), ("name", StoryValue.Of("Gazette")))).AsString());

    private static List<StoryValue> Arr(params string[] values) => values.Select(StoryValue.Of).ToList();
    private static List<StoryValue> IntArr(params long[] values) => values.Select(StoryValue.Of).ToList();
}
