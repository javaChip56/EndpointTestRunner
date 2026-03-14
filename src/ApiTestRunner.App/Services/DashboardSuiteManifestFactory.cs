using System.Security.Cryptography;
using System.Text;
using ApiTestRunner.App.Models;
using ApiTestRunner.Core.Models;

namespace ApiTestRunner.App.Services;

public static class DashboardSuiteManifestFactory
{
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

    private static DashboardEnvironmentManifest BuildEnvironmentManifest(EnvironmentDefinition environment)
    {
        return new DashboardEnvironmentManifest
        {
            Id = CreateStableId("environment", environment.Name, environment.BaseUrl),
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
            Id = CreateStableId("endpoint", environment.Name, endpoint.Method, endpoint.Path, endpoint.Name),
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
}
