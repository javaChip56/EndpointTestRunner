using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiTestRunner.Core.Services;

internal static class JsonNodeConversion
{
    public static JsonNode? ToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            string text => JsonValue.Create(text),
            bool boolean => JsonValue.Create(boolean),
            byte number => JsonValue.Create(number),
            sbyte number => JsonValue.Create(number),
            short number => JsonValue.Create(number),
            ushort number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            uint number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            ulong number => JsonValue.Create(number),
            float number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            IDictionary dictionary => ToJsonObject(dictionary),
            IEnumerable sequence when value is not string => ToJsonArray(sequence),
            _ => JsonSerializer.SerializeToNode(value)
        };
    }

    private static JsonObject ToJsonObject(IDictionary dictionary)
    {
        var jsonObject = new JsonObject();

        foreach (DictionaryEntry item in dictionary)
        {
            var key = item.Key?.ToString() ?? string.Empty;
            jsonObject[key] = ToJsonNode(item.Value);
        }

        return jsonObject;
    }

    private static JsonArray ToJsonArray(IEnumerable sequence)
    {
        var array = new JsonArray();

        foreach (var item in sequence)
        {
            array.Add(ToJsonNode(item));
        }

        return array;
    }
}
