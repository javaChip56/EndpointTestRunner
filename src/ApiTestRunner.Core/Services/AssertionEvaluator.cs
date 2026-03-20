using System.Globalization;
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

        if (assertion.NotEmpty is not null)
        {
            rulesAdded++;
            if (!TryConvertToBoolean(assertion.NotEmpty, out var expectedNotEmpty))
            {
                results.Add(CreateResult(
                    assertion.Field,
                    "notEmpty",
                    false,
                    "notEmpty must resolve to true or false."));
            }
            else
            {
                var success = expectedNotEmpty ? IsNotEmpty(actualNode) : !IsNotEmpty(actualNode);
                var expectation = expectedNotEmpty ? "notEmpty=true" : "notEmpty=false";

                results.Add(CreateResult(
                    assertion.Field,
                    "notEmpty",
                    success,
                    success
                        ? $"Field satisfied {expectation}."
                        : $"Field did not satisfy {expectation}."));
            }
        }

        if (assertion.MinCount is not null)
        {
            rulesAdded++;
            if (!TryConvertToInteger(assertion.MinCount, out var minCount))
            {
                results.Add(CreateResult(
                    assertion.Field,
                    "minCount",
                    false,
                    "minCount must resolve to an integer."));
            }
            else
            {
                var count = GetArrayCount(actualNode);
                var success = count.HasValue && count.Value >= minCount;

                results.Add(CreateResult(
                    assertion.Field,
                    "minCount",
                    success,
                    success
                        ? $"Array count was at least {minCount}."
                        : count.HasValue
                            ? $"Array count was {count.Value}, expected at least {minCount}."
                            : "Field was not an array."));
            }
        }

        if (assertion.MaxCount is not null)
        {
            rulesAdded++;
            if (!TryConvertToInteger(assertion.MaxCount, out var maxCount))
            {
                results.Add(CreateResult(
                    assertion.Field,
                    "maxCount",
                    false,
                    "maxCount must resolve to an integer."));
            }
            else
            {
                var count = GetArrayCount(actualNode);
                var success = count.HasValue && count.Value <= maxCount;

                results.Add(CreateResult(
                    assertion.Field,
                    "maxCount",
                    success,
                    success
                        ? $"Array count was at most {maxCount}."
                        : count.HasValue
                            ? $"Array count was {count.Value}, expected at most {maxCount}."
                            : "Field was not an array."));
            }
        }

        if (assertion.Count is not null)
        {
            rulesAdded++;
            if (!TryConvertToInteger(assertion.Count, out var expectedCount))
            {
                results.Add(CreateResult(
                    assertion.Field,
                    "count",
                    false,
                    "count must resolve to an integer."));
            }
            else
            {
                var count = GetArrayCount(actualNode);
                var success = count.HasValue && count.Value == expectedCount;

                results.Add(CreateResult(
                    assertion.Field,
                    "count",
                    success,
                    success
                        ? $"Array count matched {expectedCount}."
                        : count.HasValue
                            ? $"Array count was {count.Value}, expected {expectedCount}."
                            : "Field was not an array."));
            }
        }

        if (assertion.GreaterThan is not null)
        {
            rulesAdded++;
            EvaluateNumericComparison(
                assertion.Field,
                "greaterThan",
                assertion.GreaterThan,
                actualNode,
                (actual, expected) => actual > expected,
                "greater than",
                results);
        }

        if (assertion.GreaterThanOrEqual is not null)
        {
            rulesAdded++;
            EvaluateNumericComparison(
                assertion.Field,
                "greaterThanOrEqual",
                assertion.GreaterThanOrEqual,
                actualNode,
                (actual, expected) => actual >= expected,
                "greater than or equal to",
                results);
        }

        if (assertion.LessThan is not null)
        {
            rulesAdded++;
            EvaluateNumericComparison(
                assertion.Field,
                "lessThan",
                assertion.LessThan,
                actualNode,
                (actual, expected) => actual < expected,
                "less than",
                results);
        }

        if (assertion.LessThanOrEqual is not null)
        {
            rulesAdded++;
            EvaluateNumericComparison(
                assertion.Field,
                "lessThanOrEqual",
                assertion.LessThanOrEqual,
                actualNode,
                (actual, expected) => actual <= expected,
                "less than or equal to",
                results);
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

    private static void EvaluateNumericComparison(
        string field,
        string rule,
        object? expectedValue,
        JsonNode? actualNode,
        Func<decimal, decimal, bool> comparator,
        string comparisonText,
        ICollection<AssertionResult> results)
    {
        if (!TryConvertToDecimal(expectedValue, out var expectedNumber))
        {
            results.Add(CreateResult(
                field,
                rule,
                false,
                $"{rule} must resolve to a number."));
            return;
        }

        if (!TryGetNumericValue(actualNode, out var actualNumber))
        {
            results.Add(CreateResult(
                field,
                rule,
                false,
                "Field was not a number."));
            return;
        }

        var success = comparator(actualNumber, expectedNumber);
        results.Add(CreateResult(
            field,
            rule,
            success,
            success
                ? $"Value was {comparisonText} {FormatDecimal(expectedNumber)}."
                : $"Value was {FormatDecimal(actualNumber)}, expected {comparisonText} {FormatDecimal(expectedNumber)}."));
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

    private static bool TryConvertToBoolean(object? value, out bool result)
    {
        switch (value)
        {
            case bool boolean:
                result = boolean;
                return true;
            case string text when bool.TryParse(text, out var parsed):
                result = parsed;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static bool TryConvertToInteger(object? value, out int result)
    {
        switch (value)
        {
            case sbyte number:
                result = number;
                return true;
            case byte number:
                result = number;
                return true;
            case short number:
                result = number;
                return true;
            case ushort number:
                result = number;
                return true;
            case int number:
                result = number;
                return true;
            case uint number when number <= int.MaxValue:
                result = (int)number;
                return true;
            case long number when number is >= int.MinValue and <= int.MaxValue:
                result = (int)number;
                return true;
            case ulong number when number <= int.MaxValue:
                result = (int)number;
                return true;
            case double number when number >= int.MinValue && number <= int.MaxValue && Math.Abs(number % 1) < double.Epsilon:
                result = (int)number;
                return true;
            case decimal number when number >= int.MinValue && number <= int.MaxValue && decimal.Truncate(number) == number:
                result = (int)number;
                return true;
            case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryGetNumericValue(JsonNode? node, out decimal result)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<decimal>(out var decimalValue):
                result = decimalValue;
                return true;
            case JsonValue value when value.TryGetValue<double>(out var doubleValue):
                result = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                return true;
            case JsonValue value when value.TryGetValue<long>(out var longValue):
                result = longValue;
                return true;
            case JsonValue value when value.TryGetValue<int>(out var intValue):
                result = intValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryConvertToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case sbyte number:
                result = number;
                return true;
            case byte number:
                result = number;
                return true;
            case short number:
                result = number;
                return true;
            case ushort number:
                result = number;
                return true;
            case int number:
                result = number;
                return true;
            case uint number:
                result = number;
                return true;
            case long number:
                result = number;
                return true;
            case ulong number:
                result = number;
                return true;
            case float number:
                result = Convert.ToDecimal(number, CultureInfo.InvariantCulture);
                return true;
            case double number:
                result = Convert.ToDecimal(number, CultureInfo.InvariantCulture);
                return true;
            case decimal number:
                result = number;
                return true;
            case string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.############################", CultureInfo.InvariantCulture);
    }
}
