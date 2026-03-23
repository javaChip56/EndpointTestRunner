using System.Text.Json;
using ApiTestRunner.App.Models;
using ApiTestRunner.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ApiTestRunner.App.Services;

public sealed class DashboardEndpointEditorService
{
    private readonly IConfiguredTestSuiteProvider _suiteProvider;
    private readonly IDeserializer _deserializer;

    public DashboardEndpointEditorService(IConfiguredTestSuiteProvider suiteProvider)
    {
        _suiteProvider = suiteProvider;
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
        var seed = DashboardSuiteManifestFactory.CreateEditorSeed(loadedSuite.Suite, environmentId, endpointId)
            ?? throw new InvalidOperationException("The selected endpoint could not be found in the loaded YAML suite.");

        return new DashboardEndpointEditorSeed
        {
            EnvironmentId = seed.EnvironmentId,
            EnvironmentName = seed.EnvironmentName,
            EndpointId = seed.EndpointId,
            EndpointName = sourceEntry.Endpoint.Name,
            SourceFilePath = sourceEntry.FilePath,
            CurlCommand = seed.CurlCommand,
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
