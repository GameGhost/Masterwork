using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Masterwork.Engine;

// Runtime value produced by evaluating an expression. Mirrors the MWS type system: int, string,
// bool, array, and record (immutable, named-property value type). Polymorphic JSON attributes let
// SessionSave (which embeds ExprValue in every snapshot's Variables dict) round-trip through
// System.Text.Json for save/load.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(IntVal), "int")]
[JsonDerivedType(typeof(StringVal), "string")]
[JsonDerivedType(typeof(BoolVal), "bool")]
[JsonDerivedType(typeof(ArrayVal), "array")]
[JsonDerivedType(typeof(RecordVal), "record")]
public abstract record ExprValue
{
    public sealed record IntVal(long Value) : ExprValue;
    public sealed record StringVal(string Value) : ExprValue;
    public sealed record BoolVal(bool Value) : ExprValue;
    public sealed record ArrayVal(IReadOnlyList<ExprValue> Items) : ExprValue;
    public sealed record RecordVal(IReadOnlyDictionary<string, ExprValue> Properties) : ExprValue;

    public static ExprValue Of(long v) => new IntVal(v);
    public static ExprValue Of(string v) => new StringVal(v);
    public static ExprValue Of(bool v) => new BoolVal(v);
    public static ExprValue Of(IReadOnlyList<ExprValue> v) => new ArrayVal(v);

    public long AsInt() => this switch
    {
        IntVal i => i.Value,
        // Cradle stores everything as strings at runtime; arithmetic on a string var parses it.
        StringVal s when long.TryParse(s.Value, out var l) => l,
        BoolVal b => b.Value ? 1 : 0,
        _ => throw new ExprEvalException($"Cannot convert {Describe()} to int"),
    };

    public string AsString() => this switch
    {
        StringVal s => s.Value,
        IntVal i => i.Value.ToString(),
        BoolVal b => b.Value ? "1" : "0",
        _ => throw new ExprEvalException($"Cannot convert {Describe()} to string"),
    };

    public bool AsBool() => this switch
    {
        BoolVal b => b.Value,
        IntVal i => i.Value != 0,
        StringVal s => s.Value is not ("" or "0"),
        _ => throw new ExprEvalException($"Cannot convert {Describe()} to bool"),
    };

    public IReadOnlyList<ExprValue> AsArray() => this switch
    {
        ArrayVal a => a.Items,
        _ => throw new ExprEvalException($"Cannot convert {Describe()} to array"),
    };

    public IReadOnlyDictionary<string, ExprValue> AsRecord() => this switch
    {
        RecordVal r => r.Properties,
        _ => throw new ExprEvalException($"Cannot convert {Describe()} to record"),
    };

    private string Describe() => this switch
    {
        IntVal i => $"int({i.Value})",
        StringVal s => $"string(\"{s.Value}\")",
        BoolVal b => $"bool({b.Value})",
        ArrayVal => "array",
        RecordVal => "record",
        _ => GetType().Name,
    };

    // Value equality per the MWS spec: custom-typed (record) values compare by member equality;
    // everything else compares after normalizing int/string/bool per the coercion rules.
    public static bool ValueEquals(ExprValue a, ExprValue b)
    {
        if (a is RecordVal ra && b is RecordVal rb)
        {
            if (ra.Properties.Count != rb.Properties.Count) return false;
            return ra.Properties.All(kv => rb.Properties.TryGetValue(kv.Key, out var v) && ValueEquals(kv.Value, v));
        }
        if (a is ArrayVal aa && b is ArrayVal ab)
            return aa.Items.Count == ab.Items.Count && aa.Items.Zip(ab.Items, ValueEquals).All(x => x);
        if (a is RecordVal || b is RecordVal || a is ArrayVal || b is ArrayVal) return false;
        if (a is IntVal || b is IntVal)
        {
            // Compare numerically when either side parses as int; otherwise string-coerce per spec.
            if (TryAsLong(a, out var la) && TryAsLong(b, out var lb)) return la == lb;
        }
        return a.AsString() == b.AsString();
    }

    private static bool TryAsLong(ExprValue v, out long result)
    {
        switch (v)
        {
            case IntVal i: result = i.Value; return true;
            case StringVal s when long.TryParse(s.Value, out var l): result = l; return true;
            default: result = 0; return false;
        }
    }
}

public sealed class ExprEvalException(string message) : Exception(message);
