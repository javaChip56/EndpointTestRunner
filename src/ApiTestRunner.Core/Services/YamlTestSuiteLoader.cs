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
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException("No YAML test files were configured.");
        }

        var environments = new List<EnvironmentDefinition>();

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"YAML test file was not found: {filePath}", filePath);
            }

            _logger.LogInformation("Loading YAML test suite from {FilePath}", filePath);

            var yaml = await File.ReadAllTextAsync(filePath, cancellationToken);
            var document = _deserializer.Deserialize<ApiTestSuiteDefinition>(yaml) ?? new ApiTestSuiteDefinition();

            ValidateDocument(document, filePath);
            environments.AddRange(document.Environments);
        }

        if (environments.Count == 0)
        {
            throw new InvalidOperationException("No environments were found in the configured YAML files.");
        }

        return new ApiTestSuiteDefinition
        {
            Environments = environments
        };
    }

    private static void ValidateDocument(ApiTestSuiteDefinition document, string filePath)
    {
        for (var environmentIndex = 0; environmentIndex < document.Environments.Count; environmentIndex++)
        {
            var environment = document.Environments[environmentIndex];

            if (string.IsNullOrWhiteSpace(environment.Name))
            {
                throw new InvalidOperationException($"Environment at index {environmentIndex} in '{filePath}' is missing a name.");
            }

            if (string.IsNullOrWhiteSpace(environment.BaseUrl))
            {
                throw new InvalidOperationException($"Environment '{environment.Name}' in '{filePath}' is missing a baseUrl.");
            }

            for (var endpointIndex = 0; endpointIndex < environment.Endpoints.Count; endpointIndex++)
            {
                var endpoint = environment.Endpoints[endpointIndex];

                if (string.IsNullOrWhiteSpace(endpoint.Name))
                {
                    throw new InvalidOperationException($"Endpoint at index {endpointIndex} in environment '{environment.Name}' is missing a name.");
                }

                if (string.IsNullOrWhiteSpace(endpoint.Method))
                {
                    throw new InvalidOperationException($"Endpoint '{endpoint.Name}' in environment '{environment.Name}' is missing a method.");
                }

                if (string.IsNullOrWhiteSpace(endpoint.Path))
                {
                    throw new InvalidOperationException($"Endpoint '{endpoint.Name}' in environment '{environment.Name}' is missing a path.");
                }

                for (var testIndex = 0; testIndex < endpoint.Tests.Count; testIndex++)
                {
                    var test = endpoint.Tests[testIndex];

                    if (string.IsNullOrWhiteSpace(test.Name))
                    {
                        throw new InvalidOperationException($"Test at index {testIndex} on endpoint '{endpoint.Name}' is missing a name.");
                    }
                }
            }
        }
    }
}
