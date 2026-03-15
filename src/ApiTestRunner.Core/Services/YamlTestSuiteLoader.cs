using ApiTestRunner.Core.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ApiTestRunner.Core.Services;

public sealed class YamlTestSuiteLoader : IYamlTestSuiteLoader
{
    private readonly ILogger<YamlTestSuiteLoader> _logger;
    private readonly IDeserializer _deserializer;

    public YamlTestSuiteLoader(ILogger<YamlTestSuiteLoader> logger)
    {
        _logger = logger;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithAttemptingUnquotedStringTypeDeserialization()
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public async Task<ApiTestSuiteDefinition> LoadAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var files = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException("No YAML test files were configured.");
        }

        var environmentsByName = new Dictionary<string, EnvironmentDefinition>(StringComparer.OrdinalIgnoreCase);
        var deferredEndpointImports = new List<EndpointImportDefinition>();

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"YAML test file was not found: {filePath}", filePath);
            }

            _logger.LogInformation("Loading YAML test suite from {FilePath}", filePath);

            var yaml = await File.ReadAllTextAsync(filePath, cancellationToken);
            var document = _deserializer.Deserialize<ApiTestDocumentDefinition>(yaml) ?? new ApiTestDocumentDefinition();

            if (ShouldSkipEmptyDocument(yaml, document))
            {
                _logger.LogWarning("Skipping empty YAML test file: {FilePath}", filePath);
                continue;
            }

            ValidateDocumentShape(document, filePath);

            foreach (var environment in document.Environments)
            {
                ValidateEnvironment(environment, filePath);
                MergeEnvironment(environmentsByName, environment, filePath);
            }

            if (document.Endpoints.Count > 0)
            {
                foreach (var endpoint in document.Endpoints)
                {
                    ValidateEndpoint(endpoint, "(top-level)", filePath);
                }

                deferredEndpointImports.Add(new EndpointImportDefinition(
                    filePath,
                    document.TargetEnvironments,
                    document.Endpoints));
            }
        }

        if (environmentsByName.Count == 0)
        {
            throw new InvalidOperationException("No environments were found in the configured YAML files.");
        }

        ApplyEndpointImports(environmentsByName, deferredEndpointImports);

        return new ApiTestSuiteDefinition
        {
            Environments = environmentsByName.Values
                .OrderBy(environment => environment.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static void ValidateDocumentShape(ApiTestDocumentDefinition document, string filePath)
    {
        if (document.Environments.Count == 0 && document.Endpoints.Count == 0)
        {
            throw new InvalidOperationException(
                $"YAML file '{filePath}' did not define any environments or endpoints.");
        }
    }

    private static bool ShouldSkipEmptyDocument(string yaml, ApiTestDocumentDefinition document)
    {
        if (document.Environments.Count > 0 || document.Endpoints.Count > 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(yaml))
        {
            return true;
        }

        var lines = yaml
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim());

        return lines.All(line => line.Length == 0 || line == "---" || line == "..." || line.StartsWith('#'));
    }

    private static void ValidateEnvironment(EnvironmentDefinition environment, string filePath)
    {
        if (string.IsNullOrWhiteSpace(environment.Name))
        {
            throw new InvalidOperationException($"An environment in '{filePath}' is missing a name.");
        }

        if (string.IsNullOrWhiteSpace(environment.BaseUrl))
        {
            throw new InvalidOperationException($"Environment '{environment.Name}' in '{filePath}' is missing a baseUrl.");
        }

        foreach (var variable in environment.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                throw new InvalidOperationException($"Environment '{environment.Name}' in '{filePath}' has a variable with an empty name.");
            }
        }

        foreach (var endpoint in environment.Endpoints)
        {
            ValidateEndpoint(endpoint, environment.Name, filePath);
        }
    }

    private static void ValidateEndpoint(EndpointDefinition endpoint, string environmentName, string filePath)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Name))
        {
            throw new InvalidOperationException($"An endpoint in '{filePath}' for '{environmentName}' is missing a name.");
        }

        if (string.IsNullOrWhiteSpace(endpoint.Method))
        {
            throw new InvalidOperationException($"Endpoint '{endpoint.Name}' in '{filePath}' is missing a method.");
        }

        if (string.IsNullOrWhiteSpace(endpoint.Path))
        {
            throw new InvalidOperationException($"Endpoint '{endpoint.Name}' in '{filePath}' is missing a path.");
        }

        for (var testIndex = 0; testIndex < endpoint.Tests.Count; testIndex++)
        {
            var test = endpoint.Tests[testIndex];

            if (string.IsNullOrWhiteSpace(test.Name))
            {
                throw new InvalidOperationException(
                    $"Test at index {testIndex} on endpoint '{endpoint.Name}' in '{filePath}' is missing a name.");
            }
        }
    }

    private static void MergeEnvironment(
        IDictionary<string, EnvironmentDefinition> environmentsByName,
        EnvironmentDefinition incomingEnvironment,
        string filePath)
    {
        if (!environmentsByName.TryGetValue(incomingEnvironment.Name, out var existingEnvironment))
        {
            environmentsByName[incomingEnvironment.Name] = CloneEnvironment(incomingEnvironment);
            return;
        }

        if (!string.Equals(existingEnvironment.BaseUrl, incomingEnvironment.BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Environment '{incomingEnvironment.Name}' in '{filePath}' conflicts with an existing baseUrl. " +
                $"Existing: '{existingEnvironment.BaseUrl}', incoming: '{incomingEnvironment.BaseUrl}'.");
        }

        var mergedVariables = new Dictionary<string, object?>(existingEnvironment.Variables, StringComparer.OrdinalIgnoreCase);
        foreach (var variable in incomingEnvironment.Variables)
        {
            mergedVariables[variable.Key] = variable.Value;
        }

        environmentsByName[incomingEnvironment.Name] = existingEnvironment with
        {
            Variables = mergedVariables,
            Endpoints = [.. existingEnvironment.Endpoints, .. incomingEnvironment.Endpoints.Select(CloneEndpoint)]
        };
    }

    private static void ApplyEndpointImports(
        IDictionary<string, EnvironmentDefinition> environmentsByName,
        IEnumerable<EndpointImportDefinition> endpointImports)
    {
        foreach (var endpointImport in endpointImports)
        {
            var targetEnvironments = endpointImport.TargetEnvironments
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (targetEnvironments.Length == 0)
            {
                if (environmentsByName.Count == 1)
                {
                    targetEnvironments = [environmentsByName.Keys.Single()];
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Endpoint-only file '{endpointImport.FilePath}' must specify targetEnvironments when more than one environment is configured.");
                }
            }

            foreach (var environmentName in targetEnvironments)
            {
                if (!environmentsByName.TryGetValue(environmentName, out var environment))
                {
                    throw new InvalidOperationException(
                        $"Endpoint-only file '{endpointImport.FilePath}' references unknown environment '{environmentName}'.");
                }

                environmentsByName[environmentName] = environment with
                {
                    Endpoints = [.. environment.Endpoints, .. endpointImport.Endpoints.Select(CloneEndpoint)]
                };
            }
        }
    }

    private static EnvironmentDefinition CloneEnvironment(EnvironmentDefinition environment)
    {
        return environment with
        {
            Variables = new Dictionary<string, object?>(environment.Variables, StringComparer.OrdinalIgnoreCase),
            Endpoints = environment.Endpoints.Select(CloneEndpoint).ToList()
        };
    }

    private static EndpointDefinition CloneEndpoint(EndpointDefinition endpoint)
    {
        return endpoint with
        {
            PathParams = new Dictionary<string, object?>(endpoint.PathParams, StringComparer.OrdinalIgnoreCase),
            Query = new Dictionary<string, object?>(endpoint.Query, StringComparer.OrdinalIgnoreCase),
            Headers = new Dictionary<string, string>(endpoint.Headers, StringComparer.OrdinalIgnoreCase),
            Tests = endpoint.Tests.Select(CloneTest).ToList()
        };
    }

    private static TestDefinition CloneTest(TestDefinition test)
    {
        return test with
        {
            Assertions = test.Assertions.Select(CloneAssertion).ToList()
        };
    }

    private static AssertionDefinition CloneAssertion(AssertionDefinition assertion)
    {
        return assertion with
        {
            Contains = new Dictionary<string, object?>(assertion.Contains, StringComparer.OrdinalIgnoreCase)
        };
    }

    private sealed record EndpointImportDefinition(
        string FilePath,
        IReadOnlyList<string> TargetEnvironments,
        IReadOnlyList<EndpointDefinition> Endpoints);
}
