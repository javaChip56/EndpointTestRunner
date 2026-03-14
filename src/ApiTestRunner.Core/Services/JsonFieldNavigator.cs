using System.Text;
using System.Text.Json.Nodes;

namespace ApiTestRunner.Core.Services;

internal static class JsonFieldNavigator
{
    public static bool TryGetNode(JsonNode? root, string? fieldPath, out JsonNode? result)
    {
        result = root;

        if (string.IsNullOrWhiteSpace(fieldPath) || fieldPath == "$")
        {
            return root is not null;
        }

        foreach (var token in Tokenize(fieldPath))
        {
            switch (token)
            {
                case PropertyPathToken property:
                    if (result is not JsonObject jsonObject ||
                        !TryGetPropertyValue(jsonObject, property.Name, out result))
                    {
                        result = null;
                        return false;
                    }

                    break;

                case IndexPathToken index:
                    if (result is not JsonArray array || index.Index < 0 || index.Index >= array.Count)
                    {
                        result = null;
                        return false;
                    }

                    result = array[index.Index];
                    break;
            }
        }

        return result is not null;
    }

    private static bool TryGetPropertyValue(JsonObject jsonObject, string propertyName, out JsonNode? result)
    {
        if (jsonObject.TryGetPropertyValue(propertyName, out result))
        {
            return true;
        }

        foreach (var property in jsonObject)
        {
            if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                result = property.Value;
                return true;
            }
        }

        result = null;
        return false;
    }

    private static IReadOnlyList<PathToken> Tokenize(string fieldPath)
    {
        var tokens = new List<PathToken>();
        var buffer = new StringBuilder();

        for (var index = 0; index < fieldPath.Length; index++)
        {
            var character = fieldPath[index];

            if (character == '.')
            {
                FlushPropertyToken(tokens, buffer);
                continue;
            }

            if (character == '[')
            {
                FlushPropertyToken(tokens, buffer);

                var endBracket = fieldPath.IndexOf(']', index + 1);
                if (endBracket <= index + 1)
                {
                    throw new FormatException($"Invalid field path segment in '{fieldPath}'.");
                }

                var indexText = fieldPath[(index + 1)..endBracket];
                if (!int.TryParse(indexText, out var arrayIndex))
                {
                    throw new FormatException($"Array index '{indexText}' is not valid in '{fieldPath}'.");
                }

                tokens.Add(new IndexPathToken(arrayIndex));
                index = endBracket;
                continue;
            }

            buffer.Append(character);
        }

        FlushPropertyToken(tokens, buffer);
        return tokens;
    }

    private static void FlushPropertyToken(ICollection<PathToken> tokens, StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        tokens.Add(new PropertyPathToken(buffer.ToString()));
        buffer.Clear();
    }

    private abstract record PathToken;

    private sealed record PropertyPathToken(string Name) : PathToken;

    private sealed record IndexPathToken(int Index) : PathToken;
}
