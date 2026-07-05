using System.Text.Json.Serialization;

namespace Masterwork.Engine;

/// <summary>
/// Runtime value produced by evaluating an expression. Mirrors the MWS type system: int, string,
/// bool, array, and record (immutable, named-property value type). Polymorphic JSON attributes let
/// <see cref="Session.SessionSave"/> (which embeds <see cref="StoryValue"/> in every snapshot's Variables
/// dict) round-trip through <c>System.Text.Json</c> for save/load.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(IntVal), "int")]
[JsonDerivedType(typeof(StringVal), "string")]
[JsonDerivedType(typeof(BoolVal), "bool")]
[JsonDerivedType(typeof(ArrayVal), "array")]
[JsonDerivedType(typeof(RecordVal), "record")]
public abstract record StoryValue
{
    /// <summary>An integer value.</summary>
    public sealed record IntVal(long Value) : StoryValue;

    /// <summary>A string value.</summary>
    public sealed record StringVal(string Value) : StoryValue;

    /// <summary>A boolean value.</summary>
    public sealed record BoolVal(bool Value) : StoryValue;

    /// <summary>An array value.</summary>
    public sealed record ArrayVal(IReadOnlyList<StoryValue> Items) : StoryValue;

    /// <summary>A record (named-property) value.</summary>
    public sealed record RecordVal(IReadOnlyDictionary<string, StoryValue> Properties) : StoryValue;

    /// <summary>Wraps a <see cref="long"/> as an <see cref="IntVal"/>.</summary>
    public static StoryValue Of(long v) => new IntVal(v);

    /// <summary>Wraps a <see cref="string"/> as a <see cref="StringVal"/>.</summary>
    public static StoryValue Of(string v) => new StringVal(v);

    /// <summary>Wraps a <see cref="bool"/> as a <see cref="BoolVal"/>.</summary>
    public static StoryValue Of(bool v) => new BoolVal(v);

    /// <summary>Wraps a list as an <see cref="ArrayVal"/>.</summary>
    public static StoryValue Of(IReadOnlyList<StoryValue> v) => new ArrayVal(v);

    /// <summary>Converts this value to an <see cref="long"/>, parsing a string value if needed (Cradle stores everything as strings at runtime).</summary>
    /// <exception cref="StoryEvalException">The value can't be converted (an array or record).</exception>
    public long AsInt() => this switch
    {
        IntVal i => i.Value,
        StringVal s when long.TryParse(s.Value, out var l) => l,
        BoolVal b => b.Value ? 1 : 0,
        _ => throw new StoryEvalException($"Cannot convert {Describe()} to int"),
    };

    /// <summary>Converts this value to a <see cref="string"/>.</summary>
    /// <exception cref="StoryEvalException">The value can't be converted (an array or record).</exception>
    public string AsString() => this switch
    {
        StringVal s => s.Value,
        IntVal i => i.Value.ToString(),
        BoolVal b => b.Value ? "1" : "0",
        _ => throw new StoryEvalException($"Cannot convert {Describe()} to string"),
    };

    /// <summary>Converts this value to a <see cref="bool"/> (0/empty-string/false are falsy).</summary>
    /// <exception cref="StoryEvalException">The value can't be converted (an array or record).</exception>
    public bool AsBool() => this switch
    {
        BoolVal b => b.Value,
        IntVal i => i.Value != 0,
        StringVal s => s.Value is not ("" or "0"),
        _ => throw new StoryEvalException($"Cannot convert {Describe()} to bool"),
    };

    /// <summary>Returns this value's items.</summary>
    /// <exception cref="StoryEvalException">This isn't an <see cref="ArrayVal"/>.</exception>
    public IReadOnlyList<StoryValue> AsArray() => this switch
    {
        ArrayVal a => a.Items,
        _ => throw new StoryEvalException($"Cannot convert {Describe()} to array"),
    };

    /// <summary>Returns this value's properties.</summary>
    /// <exception cref="StoryEvalException">This isn't a <see cref="RecordVal"/>.</exception>
    public IReadOnlyDictionary<string, StoryValue> AsRecord() => this switch
    {
        RecordVal r => r.Properties,
        _ => throw new StoryEvalException($"Cannot convert {Describe()} to record"),
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

    /// <summary>
    /// Value equality per the MWS spec: record values compare by member equality; everything else
    /// compares after normalizing int/string/bool per the coercion rules.
    /// </summary>
    public static bool ValueEquals(StoryValue a, StoryValue b)
    {
        if (a is RecordVal ra && b is RecordVal rb)
        {
            if (ra.Properties.Count != rb.Properties.Count)
            {
                return false;
            }

            return ra.Properties.All(kv => rb.Properties.TryGetValue(kv.Key, out var v) && ValueEquals(kv.Value, v));
        }
        if (a is ArrayVal aa && b is ArrayVal ab)
        {
            return aa.Items.Count == ab.Items.Count && aa.Items.Zip(ab.Items, ValueEquals).All(x => x);
        }

        if (a is RecordVal || b is RecordVal || a is ArrayVal || b is ArrayVal)
        {
            return false;
        }

        if (a is IntVal || b is IntVal)
        {
            // Compare numerically when either side parses as int; otherwise string-coerce per spec.
            if (TryAsLong(a, out var la) && TryAsLong(b, out var lb))
            {
                return la == lb;
            }
        }
        return a.AsString() == b.AsString();
    }

    private static bool TryAsLong(StoryValue v, out long result)
    {
        switch (v)
        {
            case IntVal i: result = i.Value; return true;
            case StringVal s when long.TryParse(s.Value, out var l): result = l; return true;
            default: result = 0; return false;
        }
    }
}
