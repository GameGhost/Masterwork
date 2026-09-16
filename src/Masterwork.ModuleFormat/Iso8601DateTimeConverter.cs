using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Masterwork.ModuleFormat;

/// <summary>
/// Writes timestamps as plain ISO 8601 UTC — <c>2026-09-15T13:58:25Z</c>.
///
/// System.Text.Json's own default round-trips full precision and a numeric offset
/// (<c>2026-09-15T13:58:25.5518452+00:00</c>), which is valid ISO 8601 but noisy in a file meant to
/// be read and diffed by hand, and it varies with the writer's local offset even when the instant is
/// identical. Normalizing to UTC with whole seconds keeps a regenerated file byte-comparable when
/// nothing but the timestamp changed. Reading accepts any ISO 8601 form.
/// </summary>
public sealed class Iso8601DateTimeConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-ddTHH:mm:ssZ";

    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(
            reader.GetString() ?? throw new JsonException("Expected an ISO 8601 timestamp."),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
