using Masterwork.ModuleFormat;
using Masterwork.Engine.Expressions;
using Masterwork.Engine.Session;

namespace Masterwork.Engine;

/// <summary>
/// Two-tier variable storage: session variables (persistent, saved in timeline snapshots) and let
/// variables (passage-scoped, cleared before each render). Implements <see cref="IStoryEvalContext"/>
/// directly so it can be handed straight to <see cref="ExpressionEvaluator"/>.
/// </summary>
public sealed class VariableStore : IStoryEvalContext
{
    private readonly Dictionary<string, StoryValue> _session = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoryValue> _let = new(StringComparer.Ordinal);
    private readonly SessionPrng _prng;
    private readonly IExpressionEvaluator _evaluator;

    /// <summary>Creates a store with session variables initialized from the manifest's declared defaults.</summary>
    /// <param name="manifest">Variable declarations used to seed session defaults.</param>
    /// <param name="prng">Seeded PRNG backing <see cref="RandBetween"/>/<see cref="Shuffled"/>.</param>
    /// <param name="evaluator">Evaluator used by <see cref="ExpandTemplate"/>. Defaults to a new <see cref="ExpressionEvaluator"/> if omitted.</param>
    public VariableStore(IReadOnlyDictionary<string, VarDef> manifest, SessionPrng prng, IExpressionEvaluator? evaluator = null)
    {
        _prng = prng;
        _evaluator = evaluator ?? new ExpressionEvaluator();
        foreach (var (name, def) in manifest)
        {
            _session[name] = DefaultValueFor(def);
        }
    }

    /// <inheritdoc/>
    public StoryValue GetVariable(string name)
    {
        if (_let.TryGetValue(name, out var letVal))
        {
            return letVal;
        }

        if (_session.TryGetValue(name, out var sessVal))
        {
            return sessVal;
        }

        throw new StoryEvalException($"Unknown variable '{name}'");
    }

    /// <summary>Sets a persistent session variable.</summary>
    public void SetSessionVariable(string name, StoryValue value) => _session[name] = value;

    /// <summary>Sets a passage-scoped let variable.</summary>
    public void SetLetVariable(string name, StoryValue value) => _let[name] = value;

    /// <summary>Clears all let variables, e.g. before rendering a new passage.</summary>
    public void ClearLetScope() => _let.Clear();

    /// <inheritdoc/>
    public long RandBetween(long min, long max, string seedKey) => _prng.RandBetween(min, max, seedKey);

    /// <inheritdoc/>
    public IReadOnlyList<StoryValue> Shuffled(IReadOnlyList<StoryValue> items, string seedKey) => _prng.Shuffled(items, seedKey);

    /// <summary>Full point-in-time copy of session state, for timeline snapshots.</summary>
    public IReadOnlyDictionary<string, StoryValue> SessionSnapshot() => new Dictionary<string, StoryValue>(_session, StringComparer.Ordinal);

    /// <summary>Replaces all session state with a prior <see cref="SessionSnapshot"/> capture.</summary>
    public void RestoreSession(IReadOnlyDictionary<string, StoryValue> snapshot)
    {
        _session.Clear();
        foreach (var (k, v) in snapshot)
        {
            _session[k] = v;
        }
    }

    /// <summary>
    /// Creates a sandbox copy sharing the same PRNG (so seed-key occurrence counters keep
    /// advancing) but with independent session/let state — used for the popup transaction model,
    /// where content evaluation must stay pending until the popup is closed.
    /// </summary>
    public VariableStore Clone()
    {
        var clone = new VariableStore(new Dictionary<string, VarDef>(), _prng, _evaluator);
        clone.RestoreSession(SessionSnapshot());
        return clone;
    }

    /// <summary>
    /// Resolves <c>{varName}</c>, <c>{var.property}</c>, <c>{arr[N]}</c>, <c>{arr[^1]}</c> via the
    /// expression evaluator. <c>{icon:slug}</c> references are passed through unchanged for the
    /// App to render. Delegates to <see cref="IExpressionEvaluator.ExpandTemplate"/> — the same
    /// logic also backs quoted string literals inside expressions (see
    /// <c>Expr.StringLiteral</c>'s evaluation), so display-text templates and expr-context
    /// template strings share one implementation.
    /// </summary>
    public string ExpandTemplate(string template) => _evaluator.ExpandTemplate(template, this);

    // Canonical zero per VarKind (empty string, 0, false, empty record, empty array) whenever
    // def.Default isn't set — see VarDef.Default's remarks on when it's set at all.
    private static StoryValue DefaultValueFor(VarDef def) => def.VarType switch
    {
        VarKind.Integer => StoryValue.Of(def.Default is long l ? l : Convert.ToInt64(def.Default ?? 0L)),
        VarKind.Boolean => StoryValue.Of(def.Default as bool? ?? false),
        VarKind.Record => new StoryValue.RecordVal(new Dictionary<string, StoryValue>()),
        VarKind.StringArray or VarKind.IntArray or VarKind.BooleanArray or VarKind.RecordArray =>
            StoryValue.Of(new List<StoryValue>()),
        _ => StoryValue.Of(def.Default as string ?? ""), // VarKind.String
    };
}
