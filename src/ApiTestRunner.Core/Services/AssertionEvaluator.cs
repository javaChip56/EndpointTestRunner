using System.Text.Json.Nodes;
using ApiTestRunner.Core.Models;

namespace ApiTestRunner.Core.Services;

public sealed class AssertionEvaluator : IAssertionEvaluator
{
    public IReadOnlyList<AssertionResult> EvaluateAll(
        IReadOnlyList<AssertionDefinition> assertions,
        JsonNode? responseJson)
    {
        if (assertions.Count == 0)
        {
            return [];
        }

        var results = new List<AssertionResult>(assertions.Count * 4);

        foreach (var assertion in assertions)
        {
            Evaluate(assertion, responseJson, results);
        }

        return results;
    }

    private static void Evaluate(
        AssertionDefinition assertion,
        JsonNode? responseJson,
        ICollection<AssertionResult> results)
    {
        bool hasValue;
        JsonNode? actualNode;

        try
        {
            hasValue = JsonFieldNavigator.TryGetNode(responseJson, assertion.Field, out actualNode);
        }
        catch (FormatException exception)
        {
            results.Add(CreateResult(
                assertion.Field,
                "field",
                false,
                $"Invalid field path: {exception.Message}"));
            return;
        }

        var rulesAdded = 0;

        if (assertion.EqualsValue is not null)
        {
            rulesAdded++;
            var expectedNode = JsonNodeConversion.ToJsonNode(assertion.EqualsValue);
            var success = hasValue && JsonNode.DeepEquals(actualNode, expectedNode);

            results.Add(CreateResult(
                assertion.Field,
                "equals",
                success,
                success
                    ? "Value matched expected JSON value."
                    : $"Expected {FormatNode(expectedNode)} but found {FormatNode(actualNode)}."));
        }

        if (assertion.NotEquals is not null)
        {
            rulesAdded++;
            var unexpectedNode = JsonNodeConversion.ToJsonNode(assertion.NotEquals);
            var success = hasValue && !JsonNode.DeepEquals(actualNode, unexpectedNode);

            results.Add(CreateResult(
                assertion.Field,
                "notEquals",
                success,
                success
                    ? "Value did not match the disallowed JSON value."
                    : $"Value unexpectedly matched {FormatNode(unexpectedNode)}."));
        }

        if (!string.IsNullOrWhiteSpace(assertion.Type))
        {
            rulesAdded++;
            var actualType = GetNodeType(actualNode);
            var expectedType = assertion.Type!.Trim().ToLowerInvariant();
            var success = hasValue && string.Equals(actualType, expectedType, StringComparison.Ordinal);

            results.Add(CreateResult(
                assertion.Field,
                "type",
                success,
                success
                    ? $"Type matched '{expectedType}'."
                    : hasValue
                        ? $"Expected type '{expectedType}' but found '{actualType}'."
                        : "Field was not found."));
        }

        if (!string.IsNullOrWhiteSpace(assertion.ContainsText))
        {
            rulesAdded++;
            var actualText = TryGetString(actualNode);
            var expectedText = assertion.ContainsText!;
            var success = actualText is not null && actualText.Contains(expectedText, StringComparison.Ordinal);

            results.Add(CreateResult(
                assertion.Field,
                "containsText",
                success,
                success
                    ? $"Text contained '{expectedText}'."
                    : actualText is null
                        ? "Field was not a string."
                        : $"'{actualText}' did not contain '{expectedText}'."));
        }

        if (!string.IsNullOrWhiteSpace(assertion.StartsWith))
        {
            rulesAdded++;
            var actualText = TryGetString(actualNode);
            var expectedPrefix = assertion.StartsWith!;
            var success = actualText is not null && actualText.StartsWith(expectedPrefix, StringComparison.Ordinal);

            results.Add(CreateResult(
                assertion.Field,
                "startsWith",
                success,
                success
                    ? $"Text started with '{expectedPrefix}'."
                    : actualText is null
                        ? "Field was not a string."
                        : $"'{actualText}' did not start with '{expectedPrefix}'."));
        }

        if (!string.IsNullOrWhiteSpace(assertion.EndsWith))
        {
            rulesAdded++;
            var actualText = TryGetString(actualNode);
            var expectedSuffix = assertion.EndsWith!;
            var success = actualText is not null && actualText.EndsWith(expectedSuffix, StringComparison.Ordinal);

            results.Add(CreateResult(
                assertion.Field,
                "endsWith",
                success,
                success
                    ? $"Text ended with '{expectedSuffix}'."
                    : actualText is null
                        ? "Field was not a string."
                        : $"'{actualText}' did not end with '{expectedSuffix}'."));
        }

        if (assertion.NotEmpty.HasValue)
        {
            rulesAdded++;
            var success = assertion.NotEmpty.Value ? IsNotEmpty(actualNode) : !IsNotEmpty(actualNode);
            var expectation = assertion.NotEmpty.Value ? "notEmpty=true" : "notEmpty=false";

            results.Add(CreateResult(
                assertion.Field,
                "notEmpty",
                success,
                success
                    ? $"Field satisfied {expectation}."
                    : $"Field did not satisfy {expectation}."));
        }

        if (assertion.MinCount.HasValue)
        {
            rulesAdded++;
            var count = GetArrayCount(actualNode);
            var success = count.HasValue && count.Value >= assertion.MinCount.Value;

            results.Add(CreateResult(
                assertion.Field,
                "minCount",
                success,
                success
                    ? $"Array count was at least {assertion.MinCount.Value}."
                    : count.HasValue
                        ? $"Array count was {count.Value}, expected at least {assertion.MinCount.Value}."
                        : "Field was not an array."));
        }

        if (assertion.MaxCount.HasValue)
        {
            rulesAdded++;
            var count = GetArrayCount(actualNode);
            var success = count.HasValue && count.Value <= assertion.MaxCount.Value;

            results.Add(CreateResult(
                assertion.Field,
                "maxCount",
                success,
                success
                    ? $"Array count was at most {assertion.MaxCount.Value}."
                    : count.HasValue
                        ? $"Array count was {count.Value}, expected at most {assertion.MaxCount.Value}."
                        : "Field was not an array."));
        }

        if (assertion.Count.HasValue)
        {
            rulesAdded++;
            var count = GetArrayCount(actualNode);
            var success = count.HasValue && count.Value == assertion.Count.Value;

            results.Add(CreateResult(
                assertion.Field,
                "count",
                success,
                success
                    ? $"Array count matched {assertion.Count.Value}."
                    : count.HasValue
                        ? $"Array count was {count.Value}, expected {assertion.Count.Value}."
                        : "Field was not an array."));
        }

        if (assertion.Contains.Count > 0)
        {
            rulesAdded++;
            var success = ArrayContainsMatch(actualNode, assertion.Contains);

            results.Add(CreateResult(
                assertion.Field,
                "contains",
                success,
                success
                    ? "Array contained a matching object."
                    : "Array did not contain an object matching all expected fields."));
        }

        if (rulesAdded == 0)
        {
            results.Add(CreateResult(
                assertion.Field,
                "invalid",
                false,
                "Assertion did not define any supported validation rule."));
        }
    }

    private static AssertionResult CreateResult(string field, string rule, bool isSuccess, string message)
    {
        return new AssertionResult
        {
            Field = string.IsNullOrWhiteSpace(field) ? "$" : field,
            Rule = rule,
            IsSuccess = isSuccess,
            Message = message
        };
    }

    private static string? TryGetString(JsonNode? node)
    {
        return node switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => null
        };
    }

    private static string GetNodeType(JsonNode? node)
    {
        return node switch
        {
            null => "null",
            JsonObject => "object",
            JsonArray => "array",
            JsonValue value when value.TryGetValue<string>(out _) => "string",
            JsonValue value when value.TryGetValue<bool>(out _) => "boolean",
            JsonValue value when value.TryGetValue<double>(out _) => "number",
            JsonValue => "unknown",
            _ => "unknown"
        };
    }

    private static bool IsNotEmpty(JsonNode? node)
    {
        return node switch
        {
            null => false,
            JsonValue value when value.TryGetValue<string>(out var text) => !string.IsNullOrWhiteSpace(text),
            JsonObject obj => obj.Count > 0,
            JsonArray array => array.Count > 0,
            JsonValue => true,
            _ => false
        };
    }

    private static int? GetArrayCount(JsonNode? node)
    {
        return node is JsonArray array ? array.Count : null;
    }

    private static bool ArrayContainsMatch(JsonNode? node, IReadOnlyDictionary<string, object?> expectedFields)
    {
        if (node is not JsonArray array)
        {
            return false;
        }

        foreach (var item in array)
        {
            var matchedAllFields = true;

            foreach (var expectedField in expectedFields)
            {
                var expectedValue = JsonNodeConversion.ToJsonNode(expectedField.Value);

                if (!JsonFieldNavigator.TryGetNode(item, expectedField.Key, out var actualValue) ||
                    !JsonNode.DeepEquals(actualValue, expectedValue))
                {
                    matchedAllFields = false;
                    break;
                }
            }

            if (matchedAllFields)
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatNode(JsonNode? node)
    {
        return node?.ToJsonString() ?? "null";
    }
}
