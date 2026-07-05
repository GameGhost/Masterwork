using System.Text.RegularExpressions;
using Masterwork.ModuleFormat;

namespace Masterwork.Engine;

// Two-tier variable storage: session variables (persistent, saved in timeline snapshots) and let
// variables (passage-scoped, cleared before each render). Implements IExprContext directly so it
// can be handed straight to ExpressionEvaluator.
public sealed partial class VariableStore : IExprContext
{
    private readonly Dictionary<string, ExprValue> _session = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExprValue> _let = new(StringComparer.Ordinal);
    private readonly SessionPrng _prng;

    public VariableStore(IReadOnlyDictionary<string, VarDef> manifest, SessionPrng prng)
    {
        _prng = prng;
        foreach (var (name, def) in manifest)
        {
            _session[name] = DefaultValueFor(def);
        }
    }

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

    public void SetSessionVariable(string name, ExprValue value) => _session[name] = value;
    public void SetLetVariable(string name, ExprValue value) => _let[name] = value;
    public void ClearLetScope() => _let.Clear();

    public long RandBetween(long min, long max, string seedKey) => _prng.RandBetween(min, max, seedKey);
    public IReadOnlyList<ExprValue> Shuffled(IReadOnlyList<ExprValue> items, string seedKey) => _prng.Shuffled(items, seedKey);

    // Full point-in-time copy of session state, for timeline snapshots.
    public IReadOnlyDictionary<string, ExprValue> SessionSnapshot() => new Dictionary<string, ExprValue>(_session, StringComparer.Ordinal);

    public void RestoreSession(IReadOnlyDictionary<string, ExprValue> snapshot)
    {
        _session.Clear();
        foreach (var (k, v) in snapshot)
        {
            _session[k] = v;
        }
    }

    // A sandbox copy sharing the same PRNG (so seed-key occurrence counters keep advancing) but
    // with independent session/let state — used for the popup transaction model, where content
    // evaluation must stay pending until the popup is closed.
    public VariableStore Clone()
    {
        var clone = new VariableStore(new Dictionary<string, VarDef>(), _prng);
        clone.RestoreSession(SessionSnapshot());
        return clone;
    }

    // Resolves {varName}, {var.property}, {arr[N]}, {arr[^1]} via the expression evaluator.
    // {icon:slug} references are passed through unchanged for the App to render.
    public string ExpandTemplate(string template) =>
        PlaceholderRegex().Replace(template, m =>
        {
            var content = m.Groups[1].Value;
            if (content.StartsWith("icon:", StringComparison.Ordinal))
            {
                return m.Value;
            }

            return ExpressionEvaluator.Evaluate(content, this).AsString();
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
