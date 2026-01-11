using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulpMXFantasy.Infrastructure.ExternalApi.Models;

/// <summary>
/// JSON converter that handles decimal values that can be either strings or numbers.
/// </summary>
/// <remarks>
/// The PulpMX API sometimes returns numeric fields inconsistently:
/// - Sometimes as strings: "98.799"
/// - Sometimes as numbers: 98.799
///
/// This converter handles both cases gracefully.
/// </remarks>
public class FlexibleDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetDecimal(),
            JsonTokenType.String => decimal.TryParse(reader.GetString(), out var value) ? value : null,
            JsonTokenType.Null => null,
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
