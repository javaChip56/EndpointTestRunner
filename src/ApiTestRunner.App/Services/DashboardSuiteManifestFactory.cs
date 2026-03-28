using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Collections;
using ApiTestRunner.App.Models;
using ApiTestRunner.Core.Models;

namespace ApiTestRunner.App.Services;

public static class DashboardSuiteManifestFactory
{
    private static readonly System.Text.Json.JsonSerializerOptions CurlJsonSerializerOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static DashboardSuiteManifest Create(ApiTestSuiteDefinition suite)
    {
        ArgumentNullException.ThrowIfNull(suite);

        return new DashboardSuiteManifest
        {
            Environments = suite.Environments
                .OrderBy(environment => environment.Name, StringComparer.OrdinalIgnoreCase)
                .Select(BuildEnvironmentManifest)
                .ToArray()
        };
    }

    public static ApiTestSuiteDefinition Filter(ApiTestSuiteDefinition suite, IReadOnlyCollection<string> selectedTestIds)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(selectedTestIds);

        var selectedSet = new HashSet<string>(selectedTestIds, StringComparer.OrdinalIgnoreCase);
        var filteredEnvironments = new List<EnvironmentDefinition>();

        foreach (var environment in suite.Environments)
        {
            var filteredEndpoints = new List<EndpointDefinition>();

            for (var endpointIndex = 0; endpointIndex < environment.Endpoints.Count; endpointIndex++)
            {
                var endpoint = environment.Endpoints[endpointIndex];
                var filteredTests = new List<TestDefinition>();

                for (var testIndex = 0; testIndex < endpoint.Tests.Count; testIndex++)
                {
                    var test = endpoint.Tests[testIndex];
                    var testId = CreateTestId(environment, endpoint, test, testIndex);

                    if (selectedSet.Contains(testId))
                    {
                        filteredTests.Add(test);
                    }
                }

                if (filteredTests.Count == 0)
                {
                    continue;
                }

                filteredEndpoints.Add(endpoint with
                {
                    Tests = filteredTests
                });
            }

            if (filteredEndpoints.Count == 0)
            {
                continue;
            }

            filteredEnvironments.Add(environment with
            {
                Endpoints = filteredEndpoints
            });
        }

        return new ApiTestSuiteDefinition
        {
            Environments = filteredEnvironments
        };
    }

    public static string CreateTestId(
        EnvironmentDefinition environment,
        EndpointDefinition endpoint,
        TestDefinition test,
        int testIndex)
    {
        return CreateStableId(
            "test",
            environment.Name,
            endpoint.Method,
            endpoint.Path,
            endpoint.Name,
            test.Name,
            testIndex.ToString());
    }

    public static string CreateEnvironmentId(EnvironmentDefinition environment)
    {
        return CreateStableId("environment", environment.Name, environment.BaseUrl);
    }

    public static string CreateEndpointId(EnvironmentDefinition environment, EndpointDefinition endpoint)
    {
        return CreateStableId("endpoint", environment.Name, endpoint.Method, endpoint.Path, endpoint.Name);
    }

    public static DashboardEndpointEditorSeed? CreateEditorSeed(
        ApiTestSuiteDefinition suite,
        string environmentId,
        string endpointId)
    {
        ArgumentNullException.ThrowIfNull(suite);

        foreach (var environment in suite.Environments)
        {
            if (!string.Equals(CreateEnvironmentId(environment), environmentId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var endpoint in environment.Endpoints)
            {
                if (!string.Equals(CreateEndpointId(environment, endpoint), endpointId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return CreateEditorSeed(environment, endpoint, environmentId, endpointId);
            }
        }

        return null;
    }

    public static DashboardEndpointEditorSeed CreateEditorSeed(
        EnvironmentDefinition environment,
        EndpointDefinition endpoint,
        string environmentId,
        string endpointId)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(endpoint);

        return new DashboardEndpointEditorSeed
        {
            EnvironmentId = environmentId,
            EnvironmentName = environment.Name,
            EndpointId = endpointId,
            EndpointName = endpoint.Name,
            CurlCommand = BuildCurlCommand(environment, endpoint),
            Tests = endpoint.Tests
                .Select(CreateCurlTestDraft)
                .ToArray()
        };
    }

    private static DashboardEnvironmentManifest BuildEnvironmentManifest(EnvironmentDefinition environment)
    {
        return new DashboardEnvironmentManifest
        {
            Id = CreateEnvironmentId(environment),
            Name = environment.Name,
            BaseUrl = environment.BaseUrl,
            Endpoints = environment.Endpoints
                .OrderBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
                .Select(endpoint => BuildEndpointManifest(environment, endpoint))
                .ToArray()
        };
    }

    private static DashboardEndpointManifest BuildEndpointManifest(EnvironmentDefinition environment, EndpointDefinition endpoint)
    {
        return new DashboardEndpointManifest
        {
            Id = CreateEndpointId(environment, endpoint),
            Name = endpoint.Name,
            Method = endpoint.Method.ToUpperInvariant(),
            Path = endpoint.Path,
            Tests = endpoint.Tests
                .Select((test, index) => new DashboardTestManifest
                {
                    Id = CreateTestId(environment, endpoint, test, index),
                    Name = test.Name,
                    ExpectedStatus = test.ExpectedStatus
                })
                .ToArray()
        };
    }

    private static string CreateStableId(params string[] parts)
    {
        var raw = string.Join("|", parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static CurlTestDraft CreateCurlTestDraft(TestDefinition test)
    {
        return new CurlTestDraft
        {
            Name = test.Name,
            ExpectedStatus = test.ExpectedStatus,
            Assertions = ExpandAssertionDrafts(test.Assertions)
        };
    }

    private static IReadOnlyList<CurlAssertionDraft> ExpandAssertionDrafts(IReadOnlyList<AssertionDefinition> assertions)
    {
        var drafts = new List<CurlAssertionDraft>();

        foreach (var assertion in assertions)
        {
            AddAssertionDraftIfPresent(drafts, assertion.Field, "equals", assertion.EqualsValue);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "notEquals", assertion.NotEquals);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "type", assertion.Type);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "containsText", assertion.ContainsText);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "startsWith", assertion.StartsWith);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "endsWith", assertion.EndsWith);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "notEmpty", assertion.NotEmpty);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "greaterThan", assertion.GreaterThan);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "greaterThanOrEqual", assertion.GreaterThanOrEqual);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "lessThan", assertion.LessThan);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "lessThanOrEqual", assertion.LessThanOrEqual);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "minCount", assertion.MinCount);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "maxCount", assertion.MaxCount);
            AddAssertionDraftIfPresent(drafts, assertion.Field, "count", assertion.Count);

            if (assertion.Contains.Count > 0)
            {
                drafts.Add(new CurlAssertionDraft
                {
                    Field = assertion.Field,
                    Rule = "contains",
                    Value = assertion.Contains.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        return drafts;
    }

    private static void AddAssertionDraftIfPresent(
        ICollection<CurlAssertionDraft> drafts,
        string field,
        string rule,
        object? value)
    {
        if (value is null)
        {
            return;
        }

        drafts.Add(new CurlAssertionDraft
        {
            Field = field,
            Rule = rule,
            Value = value
        });
    }

    private static string BuildCurlCommand(EnvironmentDefinition environment, EndpointDefinition endpoint)
    {
        var command = new StringBuilder();
        command.Append("curl --request ");
        command.Append(endpoint.Method.ToUpperInvariant());
        command.Append(' ');
        command.Append('"');
        command.Append(EscapeForDoubleQuotedCurl(BuildRequestUrl(environment, endpoint)));
        command.Append('"');

        foreach (var header in endpoint.Headers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            command.Append(" \\\n  --header \"");
            command.Append(EscapeForDoubleQuotedCurl($"{header.Key}: {header.Value}"));
            command.Append('"');
        }

        if (endpoint.Body is not null)
        {
            command.Append(" \\\n  --data \"");
            command.Append(EscapeForDoubleQuotedCurl(System.Text.Json.JsonSerializer.Serialize(
                NormalizeForJsonSerialization(endpoint.Body),
                CurlJsonSerializerOptions)));
            command.Append('"');
        }

        return command.ToString();
    }

    private static string BuildRequestUrl(EnvironmentDefinition environment, EndpointDefinition endpoint)
    {
        var baseUrl = environment.BaseUrl.TrimEnd('/');
        var path = endpoint.Path.StartsWith('/') ? endpoint.Path : "/" + endpoint.Path;
        var url = new StringBuilder(baseUrl).Append(path);

        if (endpoint.Query.Count > 0)
        {
            url.Append('?');
            url.Append(string.Join("&", endpoint.Query.Select(pair => $"{pair.Key}={ConvertToCurlString(pair.Value)}")));
        }

        return url.ToString();
    }

    private static string ConvertToCurlString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            _ => System.Text.Json.JsonSerializer.Serialize(NormalizeForJsonSerialization(value), CurlJsonSerializerOptions)
        };
    }

    private static object? NormalizeForJsonSerialization(object? value)
    {
        return value switch
        {
            null => null,
            string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            IDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeForJsonSerialization(pair.Value),
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

            normalized[key] = NormalizeForJsonSerialization(entry.Value);
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
            .Select(NormalizeForJsonSerialization)
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

            dictionary[key] = NormalizeForJsonSerialization(value);
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

    private static string EscapeForDoubleQuotedCurl(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
