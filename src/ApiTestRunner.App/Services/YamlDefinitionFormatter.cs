using System.Collections;
using System.Globalization;
using ApiTestRunner.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ApiTestRunner.App.Services;

internal static class YamlDefinitionFormatter
{
    public static string SerializeApiTestDocument(ApiTestDocumentDefinition document)
    {
        return SerializeObject(BuildApiTestDocument(document));
    }

    public static string SerializeObject(object value)
    {
        var stream = new YamlStream(new YamlDocument(BuildYamlNode(value, isKey: false)));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString().Trim();
    }

    public static Dictionary<string, object?> BuildApiTestDocument(ApiTestDocumentDefinition document)
    {
        var yamlDocument = new Dictionary<string, object?>();

        if (document.Environments.Count > 0)
        {
            yamlDocument["environments"] = document.Environments
                .Select(BuildEnvironmentDocument)
                .Cast<object?>()
                .ToArray();
        }

        if (document.TargetEnvironments.Count > 0)
        {
            yamlDocument["targetEnvironments"] = document.TargetEnvironments.ToArray();
        }

        if (document.Endpoints.Count > 0)
        {
            yamlDocument["endpoints"] = document.Endpoints
                .Select(BuildEndpointDocument)
                .Cast<object?>()
                .ToArray();
        }

        return yamlDocument;
    }

    public static Dictionary<string, object?> BuildEnvironmentDocument(EnvironmentDefinition environment)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = environment.Name,
            ["baseUrl"] = environment.BaseUrl,
            ["variables"] = environment.Variables.Count == 0
                ? null
                : environment.Variables.ToDictionary(
                    pair => pair.Key,
                    pair => NormalizeForYamlValue(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
            ["endpoints"] = environment.Endpoints.Count == 0
                ? null
                : environment.Endpoints.Select(BuildEndpointDocument).Cast<object?>().ToArray()
        };
    }

    public static Dictionary<string, object?> BuildEndpointDocument(EndpointDefinition endpoint)
    {
        var document = new Dictionary<string, object?>
        {
            ["name"] = endpoint.Name,
            ["method"] = endpoint.Method,
            ["path"] = endpoint.Path
        };

        if (endpoint.PathParams.Count > 0)
        {
            document["pathParams"] = endpoint.PathParams.ToDictionary(
                pair => pair.Key,
                pair => NormalizeForYamlValue(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }

        if (endpoint.Query.Count > 0)
        {
            document["query"] = endpoint.Query.ToDictionary(
                pair => pair.Key,
                pair => NormalizeForYamlValue(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }

        if (endpoint.Headers.Count > 0)
        {
            document["headers"] = endpoint.Headers.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        if (endpoint.Body is not null)
        {
            document["body"] = NormalizeForYamlValue(endpoint.Body);
        }

        if (endpoint.Tests.Count > 0)
        {
            document["tests"] = endpoint.Tests.Select(BuildTestDocument).Cast<object?>().ToArray();
        }

        return document;
    }

    public static Dictionary<string, object?> BuildTestDocument(TestDefinition test)
    {
        var document = new Dictionary<string, object?>
        {
            ["name"] = test.Name,
            ["expectedStatus"] = test.ExpectedStatus
        };

        if (test.Assertions.Count > 0)
        {
            document["assertions"] = test.Assertions.Select(BuildAssertionDocument).Cast<object?>().ToArray();
        }

        return document;
    }

    public static Dictionary<string, object?> BuildAssertionDocument(AssertionDefinition assertion)
    {
        var document = new Dictionary<string, object?>
        {
            ["field"] = assertion.Field
        };

        AddIfPresent(document, "equals", assertion.EqualsValue);
        AddIfPresent(document, "notEquals", assertion.NotEquals);
        AddIfPresent(document, "type", assertion.Type);
        AddIfPresent(document, "containsText", assertion.ContainsText);
        AddIfPresent(document, "startsWith", assertion.StartsWith);
        AddIfPresent(document, "endsWith", assertion.EndsWith);
        AddIfPresent(document, "notEmpty", assertion.NotEmpty);
        AddIfPresent(document, "minCount", assertion.MinCount);
        AddIfPresent(document, "maxCount", assertion.MaxCount);
        AddIfPresent(document, "count", assertion.Count);
        AddIfPresent(document, "greaterThan", assertion.GreaterThan);
        AddIfPresent(document, "greaterThanOrEqual", assertion.GreaterThanOrEqual);
        AddIfPresent(document, "lessThan", assertion.LessThan);
        AddIfPresent(document, "lessThanOrEqual", assertion.LessThanOrEqual);

        if (assertion.Contains.Count > 0)
        {
            document["contains"] = assertion.Contains.ToDictionary(
                pair => pair.Key,
                pair => NormalizeForYamlValue(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }

        return document;
    }

    private static void AddIfPresent(IDictionary<string, object?> document, string key, object? value)
    {
        if (value is not null)
        {
            document[key] = NormalizeForYamlValue(value);
        }
    }

    public static object? NormalizeForYamlValue(object? value)
    {
        return value switch
        {
            null => null,
            string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            IDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeForYamlValue(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            IDictionary dictionary => NormalizeNonGenericDictionary(dictionary),
            IEnumerable sequence when value is not string => NormalizeEnumerable(sequence),
            _ => value
        };
    }

    private static Dictionary<string, object?> NormalizeNonGenericDictionary(IDictionary dictionary)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in dictionary)
        {
            var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            normalized[key] = NormalizeForYamlValue(entry.Value);
        }

        return normalized;
    }

    private static object NormalizeEnumerable(IEnumerable sequence)
    {
        var items = sequence.Cast<object?>().ToArray();

        if (TryNormalizeKeyValuePairSequence(items, out var dictionary))
        {
            return dictionary;
        }

        return items
            .Select(NormalizeForYamlValue)
            .ToList();
    }

    private static bool TryNormalizeKeyValuePairSequence(
        IReadOnlyList<object?> items,
        out Dictionary<string, object?> dictionary)
    {
        dictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (items.Count == 0)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (!TryReadKeyValuePair(item, out var key, out var value))
            {
                dictionary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                return false;
            }

            dictionary[key] = NormalizeForYamlValue(value);
        }

        return true;
    }

    private static bool TryReadKeyValuePair(object? item, out string key, out object? value)
    {
        key = string.Empty;
        value = null;

        if (item is null)
        {
            return false;
        }

        if (item is DictionaryEntry dictionaryEntry)
        {
            key = Convert.ToString(dictionaryEntry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
            value = dictionaryEntry.Value;
            return !string.IsNullOrWhiteSpace(key);
        }

        if (TryReadTupleStylePair(item, out key, out value))
        {
            return true;
        }

        var type = item.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
        {
            return false;
        }

        var keyProperty = type.GetProperty("Key");
        var valueProperty = type.GetProperty("Value");
        if (keyProperty is null || valueProperty is null)
        {
            return false;
        }

        key = Convert.ToString(keyProperty.GetValue(item), CultureInfo.InvariantCulture) ?? string.Empty;
        value = valueProperty.GetValue(item);
        return !string.IsNullOrWhiteSpace(key);
    }

    private static bool TryReadTupleStylePair(object item, out string key, out object? value)
    {
        key = string.Empty;
        value = null;

        if (item is string || item is not IEnumerable sequence)
        {
            return false;
        }

        var values = sequence.Cast<object?>().ToArray();
        if (values.Length != 2)
        {
            return false;
        }

        key = Convert.ToString(values[0], CultureInfo.InvariantCulture) ?? string.Empty;
        value = values[1];
        return !string.IsNullOrWhiteSpace(key);
    }

    private static YamlNode BuildYamlNode(object? value, bool isKey)
    {
        return value switch
        {
            null => new YamlScalarNode("null"),
            string text => new YamlScalarNode(text)
            {
                Style = isKey ? ScalarStyle.Plain : ScalarStyle.DoubleQuoted
            },
            bool boolean => new YamlScalarNode(boolean ? "true" : "false"),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => new YamlScalarNode(Convert.ToString(value, CultureInfo.InvariantCulture)),
            IDictionary<string, object?> dictionary => BuildMappingNode(dictionary),
            IEnumerable<object?> sequence => BuildSequenceNode(sequence),
            IEnumerable sequence when value is not string => BuildSequenceNode(sequence.Cast<object?>()),
            _ => new YamlScalarNode(Convert.ToString(value, CultureInfo.InvariantCulture))
            {
                Style = ScalarStyle.DoubleQuoted
            }
        };
    }

    private static YamlMappingNode BuildMappingNode(IEnumerable<KeyValuePair<string, object?>> values)
    {
        var mappingNode = new YamlMappingNode();

        foreach (var pair in values)
        {
            if (pair.Value is null)
            {
                continue;
            }

            mappingNode.Add(BuildYamlNode(pair.Key, isKey: true), BuildYamlNode(pair.Value, isKey: false));
        }

        return mappingNode;
    }

    private static YamlSequenceNode BuildSequenceNode(IEnumerable<object?> values)
    {
        var sequenceNode = new YamlSequenceNode();

        foreach (var item in values)
        {
            sequenceNode.Add(BuildYamlNode(item, isKey: false));
        }

        return sequenceNode;
    }
}
