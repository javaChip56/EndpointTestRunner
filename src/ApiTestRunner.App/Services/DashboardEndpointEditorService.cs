using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ApiTestRunner.App.Models;
using ApiTestRunner.Core.Models;
using ApiTestRunner.Core.Services;
using Microsoft.AspNetCore.WebUtilities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ApiTestRunner.App.Services;

public sealed class DashboardEndpointEditorService
{
    private static readonly JsonSerializerOptions CurlJsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IConfiguredTestSuiteProvider _suiteProvider;
    private readonly IVariableResolver _variableResolver;
    private readonly IDeserializer _deserializer;

    public DashboardEndpointEditorService(
        IConfiguredTestSuiteProvider suiteProvider,
        IVariableResolver variableResolver)
    {
        _suiteProvider = suiteProvider;
        _variableResolver = variableResolver;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithAttemptingUnquotedStringTypeDeserialization()
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public async Task<DashboardEndpointEditorSeed> GetEditorSeedAsync(
        string environmentId,
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        var loadedSuite = await _suiteProvider.LoadAsync(cancellationToken);
        var sourceEntry = await ResolveSourceEntryAsync(loadedSuite, environmentId, endpointId, cancellationToken);
        var seed = DashboardSuiteManifestFactory.CreateEditorSeed(
            sourceEntry.Environment,
            sourceEntry.Endpoint,
            environmentId,
            endpointId);

        return new DashboardEndpointEditorSeed
        {
            EnvironmentId = seed.EnvironmentId,
            EnvironmentName = seed.EnvironmentName,
            EndpointId = seed.EndpointId,
            EndpointName = sourceEntry.Endpoint.Name,
            SourceFilePath = sourceEntry.FilePath,
            CurlCommand = BuildResolvedCurlCommand(sourceEntry.Environment, sourceEntry.Endpoint),
            Tests = seed.Tests
        };
    }

    public async Task<DashboardEndpointSaveResponse> SaveAsync(
        DashboardEndpointSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.EnvironmentId) || string.IsNullOrWhiteSpace(request.EndpointId))
        {
            throw new InvalidOperationException("The endpoint editor is missing the selected endpoint identity.");
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            throw new InvalidOperationException("Paste a cURL command before saving the endpoint.");
        }

        var loadedSuite = await _suiteProvider.LoadAsync(cancellationToken);
        var sourceEntry = await ResolveSourceEntryAsync(loadedSuite, request.EnvironmentId, request.EndpointId, cancellationToken);
        var parsedRequest = CurlRequestParser.Parse(request.Command);
        var environment = loadedSuite.Suite.Environments
            .FirstOrDefault(candidate =>
                string.Equals(DashboardSuiteManifestFactory.CreateEnvironmentId(candidate), request.EnvironmentId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected environment could not be found in the loaded YAML suite.");
        var environmentMatch = CurlRequestParser.TryMatchEnvironment(environment, parsedRequest.Url)
            ?? throw new InvalidOperationException(
                $"The edited cURL command no longer matches environment '{environment.Name}' ({environment.BaseUrl}).");

        var endpointName = string.IsNullOrWhiteSpace(request.EndpointName)
            ? sourceEntry.Endpoint.Name
            : request.EndpointName.Trim();
        var endpointPath = environmentMatch.RelativePath;

        var updatedEndpoint = new EndpointDefinition
        {
            Name = endpointName,
            Method = parsedRequest.Method,
            Path = endpointPath,
            PathParams = FilterPathParams(sourceEntry.Endpoint.PathParams, endpointPath),
            Query = parsedRequest.Query.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.OrdinalIgnoreCase),
            Headers = parsedRequest.Headers
                .Where(pair => !string.Equals(pair.Key, "Host", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(pair.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            Body = parsedRequest.Body,
            Tests = BuildTests(request.Tests, endpointName)
        };

        var updatedDocument = ReplaceEndpointInDocument(sourceEntry, updatedEndpoint);
        var yaml = YamlDefinitionFormatter.SerializeApiTestDocument(updatedDocument) + Environment.NewLine;
        await File.WriteAllTextAsync(sourceEntry.FilePath, yaml, cancellationToken);

        return new DashboardEndpointSaveResponse
        {
            EnvironmentId = request.EnvironmentId,
            EndpointId = DashboardSuiteManifestFactory.CreateEndpointId(environment, updatedEndpoint),
            EndpointName = updatedEndpoint.Name,
            FilePath = sourceEntry.FilePath,
            SavedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static Dictionary<string, object?> FilterPathParams(
        IReadOnlyDictionary<string, object?> existingPathParams,
        string endpointPath)
    {
        var placeholders = endpointPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.StartsWith('{') && segment.EndsWith('}') && segment.Length > 2)
            .Select(segment => segment[1..^1])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return existingPathParams
            .Where(pair => placeholders.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private string BuildResolvedCurlCommand(EnvironmentDefinition environment, EndpointDefinition endpoint)
    {
        var command = new StringBuilder();
        command.Append("curl --request ");
        command.Append(endpoint.Method.ToUpperInvariant());
        command.Append(' ');
        command.Append('"');
        command.Append(EscapeForDoubleQuotedCurl(BuildResolvedRequestUrl(environment, endpoint)));
        command.Append('"');

        foreach (var header in endpoint.Headers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var resolvedHeaderValue = _variableResolver.ResolveRequiredString(header.Value, environment, $"header '{header.Key}'");
            command.Append(" \\\n  --header \"");
            command.Append(EscapeForDoubleQuotedCurl($"{header.Key}: {resolvedHeaderValue}"));
            command.Append('"');
        }

        if (endpoint.Body is not null)
        {
            var resolvedBody = _variableResolver.ResolveValue(endpoint.Body, environment);
            command.Append(" \\\n  --data \"");
            command.Append(EscapeForDoubleQuotedCurl(JsonSerializer.Serialize(
                NormalizeForJsonSerialization(resolvedBody),
                CurlJsonSerializerOptions)));
            command.Append('"');
        }

        return command.ToString();
    }

    private string BuildResolvedRequestUrl(EnvironmentDefinition environment, EndpointDefinition endpoint)
    {
        var resolvedBaseUrl = _variableResolver.ResolveRequiredString(environment.BaseUrl, environment, "baseUrl");
        var resolvedPath = ResolvePath(endpoint.Path, endpoint.PathParams, environment);

        if (!Uri.TryCreate(resolvedBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Environment '{environment.Name}' has an invalid baseUrl: {resolvedBaseUrl}");
        }

        var requestUrl = new Uri(baseUri, resolvedPath).ToString();
        if (endpoint.Query.Count == 0)
        {
            return requestUrl;
        }

        var query = endpoint.Query.ToDictionary(
            pair => pair.Key,
            pair => (string?)ConvertToCurlString(_variableResolver.ResolveValue(pair.Value, environment)),
            StringComparer.OrdinalIgnoreCase);

        return QueryHelpers.AddQueryString(requestUrl, query);
    }

    private string ResolvePath(string path, IReadOnlyDictionary<string, object?> pathParams, EnvironmentDefinition environment)
    {
        var resolvedPath = _variableResolver.ResolveRequiredString(path, environment, "path");

        foreach (var pathParam in pathParams)
        {
            var token = $"{{{pathParam.Key}}}";
            var resolvedValue = _variableResolver.ResolveValue(pathParam.Value, environment);
            resolvedPath = resolvedPath.Replace(token, Uri.EscapeDataString(ConvertToCurlString(resolvedValue)), StringComparison.Ordinal);
        }

        return resolvedPath;
    }

    private static List<TestDefinition> BuildTests(
        IReadOnlyList<CurlTestDraft> drafts,
        string endpointName)
    {
        var normalizedDrafts = drafts
            .Where(test => !string.IsNullOrWhiteSpace(test.Name))
            .ToArray();

        if (normalizedDrafts.Length == 0)
        {
            normalizedDrafts =
            [
                new CurlTestDraft
                {
                    Name = $"{endpointName} should return success",
                    ExpectedStatus = 200,
                    Assertions = []
                }
            ];
        }

        return normalizedDrafts
            .Select(draft => new TestDefinition
            {
                Name = draft.Name.Trim(),
                ExpectedStatus = draft.ExpectedStatus <= 0 ? 200 : draft.ExpectedStatus,
                Assertions = draft.Assertions
                    .Where(assertion => !string.IsNullOrWhiteSpace(assertion.Field) && !string.IsNullOrWhiteSpace(assertion.Rule))
                    .Select(BuildAssertion)
                    .ToList()
            })
            .ToList();
    }

    private static AssertionDefinition BuildAssertion(CurlAssertionDraft draft)
    {
        var normalizedValue = ConvertAssertionValue(draft.Value);

        return draft.Rule switch
        {
            "equals" => new AssertionDefinition { Field = draft.Field, EqualsValue = normalizedValue },
            "notEquals" => new AssertionDefinition { Field = draft.Field, NotEquals = normalizedValue },
            "type" => new AssertionDefinition { Field = draft.Field, Type = normalizedValue?.ToString() },
            "containsText" => new AssertionDefinition { Field = draft.Field, ContainsText = normalizedValue?.ToString() },
            "startsWith" => new AssertionDefinition { Field = draft.Field, StartsWith = normalizedValue?.ToString() },
            "endsWith" => new AssertionDefinition { Field = draft.Field, EndsWith = normalizedValue?.ToString() },
            "notEmpty" => new AssertionDefinition { Field = draft.Field, NotEmpty = normalizedValue },
            "greaterThan" => new AssertionDefinition { Field = draft.Field, GreaterThan = normalizedValue },
            "greaterThanOrEqual" => new AssertionDefinition { Field = draft.Field, GreaterThanOrEqual = normalizedValue },
            "lessThan" => new AssertionDefinition { Field = draft.Field, LessThan = normalizedValue },
            "lessThanOrEqual" => new AssertionDefinition { Field = draft.Field, LessThanOrEqual = normalizedValue },
            "minCount" => new AssertionDefinition { Field = draft.Field, MinCount = normalizedValue },
            "maxCount" => new AssertionDefinition { Field = draft.Field, MaxCount = normalizedValue },
            "count" => new AssertionDefinition { Field = draft.Field, Count = normalizedValue },
            "contains" => new AssertionDefinition
            {
                Field = draft.Field,
                Contains = normalizedValue as Dictionary<string, object?> ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            },
            _ => throw new InvalidOperationException($"Unsupported assertion rule '{draft.Rule}'.")
        };
    }

    private static object? ConvertAssertionValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement element => ConvertJsonElement(element),
            Dictionary<string, object?> dictionary => new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase),
            _ => value
        };
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText())?
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(element.GetRawText()),
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static string ConvertToCurlString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => JsonSerializer.Serialize(NormalizeForJsonSerialization(value), CurlJsonSerializerOptions)
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

    private static ApiTestDocumentDefinition ReplaceEndpointInDocument(SourceEntry sourceEntry, EndpointDefinition endpoint)
    {
        if (sourceEntry.EnvironmentIndex is int environmentIndex)
        {
            var environments = sourceEntry.Document.Environments.ToList();
            var environment = environments[environmentIndex];
            var endpoints = environment.Endpoints.ToList();
            endpoints[sourceEntry.EndpointIndex] = endpoint;
            environments[environmentIndex] = environment with { Endpoints = endpoints };

            return sourceEntry.Document with
            {
                Environments = environments
            };
        }

        var topLevelEndpoints = sourceEntry.Document.Endpoints.ToList();
        topLevelEndpoints[sourceEntry.EndpointIndex] = endpoint;

        return sourceEntry.Document with
        {
            Endpoints = topLevelEndpoints
        };
    }

    private async Task<SourceEntry> ResolveSourceEntryAsync(
        LoadedTestSuite loadedSuite,
        string environmentId,
        string endpointId,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in loadedSuite.FilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var yaml = await File.ReadAllTextAsync(filePath, cancellationToken);
            var document = _deserializer.Deserialize<ApiTestDocumentDefinition>(yaml) ?? new ApiTestDocumentDefinition();

            for (var environmentIndex = 0; environmentIndex < document.Environments.Count; environmentIndex++)
            {
                var environment = document.Environments[environmentIndex];
                var aggregateEnvironment = loadedSuite.Suite.Environments
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, environment.Name, StringComparison.OrdinalIgnoreCase))
                    ?? environment;

                if (!string.Equals(DashboardSuiteManifestFactory.CreateEnvironmentId(aggregateEnvironment), environmentId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (var endpointIndex = 0; endpointIndex < environment.Endpoints.Count; endpointIndex++)
                {
                    var endpoint = environment.Endpoints[endpointIndex];
                    if (string.Equals(DashboardSuiteManifestFactory.CreateEndpointId(aggregateEnvironment, endpoint), endpointId, StringComparison.OrdinalIgnoreCase))
                    {
                        return new SourceEntry(filePath, document, aggregateEnvironment, endpoint, environmentIndex, endpointIndex);
                    }
                }
            }

            if (document.Endpoints.Count == 0)
            {
                continue;
            }

            var targetEnvironmentNames = ResolveTargetEnvironmentNames(document, loadedSuite.Suite);
            foreach (var targetEnvironmentName in targetEnvironmentNames)
            {
                var aggregateEnvironment = loadedSuite.Suite.Environments
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, targetEnvironmentName, StringComparison.OrdinalIgnoreCase));

                if (aggregateEnvironment is null ||
                    !string.Equals(DashboardSuiteManifestFactory.CreateEnvironmentId(aggregateEnvironment), environmentId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (var endpointIndex = 0; endpointIndex < document.Endpoints.Count; endpointIndex++)
                {
                    var endpoint = document.Endpoints[endpointIndex];
                    if (string.Equals(DashboardSuiteManifestFactory.CreateEndpointId(aggregateEnvironment, endpoint), endpointId, StringComparison.OrdinalIgnoreCase))
                    {
                        return new SourceEntry(filePath, document, aggregateEnvironment, endpoint, null, endpointIndex);
                    }
                }
            }
        }

        throw new InvalidOperationException("The selected endpoint could not be mapped back to a YAML file.");
    }

    private static IReadOnlyList<string> ResolveTargetEnvironmentNames(
        ApiTestDocumentDefinition document,
        ApiTestSuiteDefinition suite)
    {
        var targetEnvironmentNames = document.TargetEnvironments
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (targetEnvironmentNames.Length > 0)
        {
            return targetEnvironmentNames;
        }

        return suite.Environments.Count == 1
            ? [suite.Environments[0].Name]
            : [];
    }

    private sealed record SourceEntry(
        string FilePath,
        ApiTestDocumentDefinition Document,
        EnvironmentDefinition Environment,
        EndpointDefinition Endpoint,
        int? EnvironmentIndex,
        int EndpointIndex);
}
