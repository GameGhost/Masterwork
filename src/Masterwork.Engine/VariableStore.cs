using System.Text.RegularExpressions;
using Masterwork.ModuleFormat;

namespace Masterwork.Engine;

/// <summary>
/// Two-tier variable storage: session variables (persistent, saved in timeline snapshots) and let
/// variables (passage-scoped, cleared before each render). Implements <see cref="IExprContext"/>
/// directly so it can be handed straight to <see cref="ExpressionEvaluator"/>.
/// </summary>
public sealed partial class VariableStore : IExprContext
{
    private readonly Dictionary<string, ExprValue> _session = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExprValue> _let = new(StringComparer.Ordinal);
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
    public ExprValue GetVariable(string name)
    {
        if (_let.TryGetValue(name, out var letVal))
        {
            return letVal;
        }

        if (_session.TryGetValue(name, out var sessVal))
        {
            return sessVal;
        }

        throw new ExprEvalException($"Unknown variable '{name}'");
    }

    /// <summary>Sets a persistent session variable.</summary>
    public void SetSessionVariable(string name, ExprValue value) => _session[name] = value;

    /// <summary>Sets a passage-scoped let variable.</summary>
    public void SetLetVariable(string name, ExprValue value) => _let[name] = value;

    /// <summary>Clears all let variables, e.g. before rendering a new passage.</summary>
    public void ClearLetScope() => _let.Clear();

    /// <inheritdoc/>
    public long RandBetween(long min, long max, string seedKey) => _prng.RandBetween(min, max, seedKey);

    /// <inheritdoc/>
    public IReadOnlyList<ExprValue> Shuffled(IReadOnlyList<ExprValue> items, string seedKey) => _prng.Shuffled(items, seedKey);

    /// <summary>Full point-in-time copy of session state, for timeline snapshots.</summary>
    public IReadOnlyDictionary<string, ExprValue> SessionSnapshot() => new Dictionary<string, ExprValue>(_session, StringComparer.Ordinal);

    /// <summary>Replaces all session state with a prior <see cref="SessionSnapshot"/> capture.</summary>
    public void RestoreSession(IReadOnlyDictionary<string, ExprValue> snapshot)
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
    /// App to render.
    /// </summary>
    public string ExpandTemplate(string template) =>
        PlaceholderRegex().Replace(template, m =>
        {
            var content = m.Groups[1].Value;
            if (content.StartsWith("icon:", StringComparison.Ordinal))
            {
                return m.Value;
            }

            return _evaluator.Evaluate(content, this).AsString();
        });

    private static ExprValue DefaultValueFor(VarDef def) => def.VarType switch
    {
        "int" => ExprValue.Of(def.Default is long l ? l : Convert.ToInt64(def.Default ?? 0L)),
        "array" => ExprValue.Of(new List<ExprValue>()),
        _ => ExprValue.Of(def.Default as string ?? ""),
    };

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex PlaceholderRegex();
}
