using ApiTestRunner.App.Models;
using ApiTestRunner.App.Services;
using ApiTestRunner.Core.Models;
using ApiTestRunner.Core.Services;
using Microsoft.Extensions.Configuration;

namespace ApiTestRunner.App.Tests;

public sealed class DashboardEndpointEditorServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public DashboardEndpointEditorServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiTestRunner.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task GetEditorSeedAsync_ReturnsSourceFilePathAndEndpointName()
    {
        var (provider, environment, endpoint, _, endpointFilePath) = CreateProvider();
        var service = CreateService(provider);

        var seed = await service.GetEditorSeedAsync(
            DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
            DashboardSuiteManifestFactory.CreateEndpointId(environment, endpoint));

        Assert.Equal("Get Accounts", seed.EndpointName);
        Assert.Equal(endpointFilePath, seed.SourceFilePath);
        Assert.Contains("curl --request GET", seed.CurlCommand);
    }

    [Fact]
    public async Task GetEditorSeedAsync_ReturnsEndpointNameForEndpointOnlyFile()
    {
        var environmentFilePath = Path.Combine(_tempDirectory, "petstore.io.yaml");
        var endpointFilePath = Path.Combine(_tempDirectory, "pet.yaml");

        File.WriteAllText(environmentFilePath, """
            environments:
              - name: "PetstoreSwaggerIo"
                baseUrl: "https://petstore.swagger.io"
            """);

        File.WriteAllText(endpointFilePath, """
            targetEnvironments:
              - "PetstoreSwaggerIo"
            endpoints:
              - name: "GET Store Inventory"
                method: "GET"
                path: "/v2/store/inventory"
                headers:
                  accept: "application/json"
                  api_key: "special-key"
                tests:
                  - name: "Test 1"
                    expectedStatus: 200
            """);

        var endpoint = new EndpointDefinition
        {
            Name = "GET Store Inventory",
            Method = "GET",
            Path = "/v2/store/inventory",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accept"] = "application/json",
                ["api_key"] = "special-key"
            },
            Tests =
            [
                new TestDefinition
                {
                    Name = "Test 1",
                    ExpectedStatus = 200
                }
            ]
        };

        var environment = new EnvironmentDefinition
        {
            Name = "PetstoreSwaggerIo",
            BaseUrl = "https://petstore.swagger.io",
            Endpoints = [endpoint]
        };

        var provider = new StubConfiguredTestSuiteProvider(new LoadedTestSuite(
            new ApiTestSuiteDefinition
            {
                Environments = [environment]
            },
            [environmentFilePath, endpointFilePath]));

        var service = CreateService(provider);

        var seed = await service.GetEditorSeedAsync(
            DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
            DashboardSuiteManifestFactory.CreateEndpointId(environment, endpoint));

        Assert.Equal("GET Store Inventory", seed.EndpointName);
        Assert.Equal(endpointFilePath, seed.SourceFilePath);
        Assert.Contains("https://petstore.swagger.io/v2/store/inventory", seed.CurlCommand);
    }

    [Fact]
    public async Task SaveAsync_UpdatesEndpointYamlFile()
    {
        var (provider, environment, endpoint, _, endpointFilePath) = CreateProvider();
        var service = CreateService(provider);

        var response = await service.SaveAsync(new DashboardEndpointSaveRequest
        {
            EnvironmentId = DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
            EndpointId = DashboardSuiteManifestFactory.CreateEndpointId(environment, endpoint),
            EndpointName = "Get Account List",
            Command = """
                curl --request POST "https://api.example.com/accounts?roleId=106" \
                  --header "Content-Type: application/json" \
                  --data "{\"page\":1}"
                """,
            Tests =
            [
                new CurlTestDraft
                {
                    Name = "Account list should return data",
                    ExpectedStatus = 200,
                    Assertions =
                    [
                        new CurlAssertionDraft
                        {
                            Field = "statusCode",
                            Rule = "equals",
                            Value = 1
                        }
                    ]
                }
            ]
        });

        var savedYaml = await File.ReadAllTextAsync(endpointFilePath);

        Assert.Equal(endpointFilePath, response.FilePath);
        Assert.Equal("Get Account List", response.EndpointName);
        Assert.Contains("targetEnvironments:", savedYaml);
        Assert.Contains("- \"Local\"", savedYaml);
        Assert.Contains("name: \"Get Account List\"", savedYaml);
        Assert.Contains("method: \"POST\"", savedYaml);
        Assert.Contains("path: \"/accounts\"", savedYaml);
        Assert.Contains("roleId: \"106\"", savedYaml);
        Assert.Contains("page: 1", savedYaml);
        Assert.Contains("field: \"statusCode\"", savedYaml);
        Assert.Contains("equals: 1", savedYaml);
    }

    [Fact]
    public async Task SaveAsync_DecodesEncodedRoutePlaceholdersInEndpointPath()
    {
        var environmentFilePath = Path.Combine(_tempDirectory, "customer-environment.yaml");
        var endpointFilePath = Path.Combine(_tempDirectory, "customer-endpoint.yaml");

        File.WriteAllText(environmentFilePath, """
            environments:
              - name: "Local"
                baseUrl: "https://api.example.com"
            """);

        File.WriteAllText(endpointFilePath, """
            targetEnvironments:
              - "Local"
            endpoints:
              - name: "Get Customer Details"
                method: "GET"
                path: "/sample-api/customers/{customerId}"
                tests:
                  - name: "Customer lookup should succeed"
                    expectedStatus: 200
            """);

        var endpoint = new EndpointDefinition
        {
            Name = "Get Customer Details",
            Method = "GET",
            Path = "/sample-api/customers/{customerId}",
            Tests =
            [
                new TestDefinition
                {
                    Name = "Customer lookup should succeed",
                    ExpectedStatus = 200
                }
            ]
        };

        var environment = new EnvironmentDefinition
        {
            Name = "Local",
            BaseUrl = "https://api.example.com",
            Endpoints = [endpoint]
        };

        var provider = new StubConfiguredTestSuiteProvider(new LoadedTestSuite(
            new ApiTestSuiteDefinition
            {
                Environments = [environment]
            },
            [environmentFilePath, endpointFilePath]));

        var service = CreateService(provider);

        await service.SaveAsync(new DashboardEndpointSaveRequest
        {
            EnvironmentId = DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
            EndpointId = DashboardSuiteManifestFactory.CreateEndpointId(environment, endpoint),
            EndpointName = "Get Customer Details",
            Command = "curl --request GET \"https://api.example.com/sample-api/customers/%7BcustomerId%7D\"",
            Tests =
            [
                new CurlTestDraft
                {
                    Name = "Customer profile should include tags",
                    ExpectedStatus = 200
                }
            ]
        });

        var savedYaml = await File.ReadAllTextAsync(endpointFilePath);

        Assert.Contains("path: \"/sample-api/customers/{customerId}\"", savedYaml);
        Assert.DoesNotContain("%7BcustomerId%7D", savedYaml);
    }

    [Fact]
    public async Task GetEditorSeedAsync_ResolvesEnvironmentVariablesInCurlCommand()
    {
        var environmentFilePath = Path.Combine(_tempDirectory, "resolved-environment.yaml");
        var endpointFilePath = Path.Combine(_tempDirectory, "resolved-endpoint.yaml");

        File.WriteAllText(environmentFilePath, """
            environments:
              - name: "Local"
                baseUrl: "https://api.example.com"
                variables:
                  userId: "233083"
                  roleId: "106"
            """);

        File.WriteAllText(endpointFilePath, """
            targetEnvironments:
              - "Local"
            endpoints:
              - name: "Get Account List"
                method: "POST"
                path: "/accounts"
                query:
                  UserId: "{{var:userId}}"
                headers:
                  X-Role: "{{var:roleId}}"
                body:
                  roleId: "{{var:roleId}}"
                tests:
                  - name: "Returns data"
                    expectedStatus: 200
            """);

        var environment = new EnvironmentDefinition
        {
            Name = "Local",
            BaseUrl = "https://api.example.com",
            Variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["userId"] = "233083",
                ["roleId"] = "106"
            },
            Endpoints =
            [
                new EndpointDefinition
                {
                    Name = "Get Account List",
                    Method = "POST",
                    Path = "/accounts",
                    Query = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["UserId"] = "{{var:userId}}"
                    },
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["X-Role"] = "{{var:roleId}}"
                    },
                    Body = new Dictionary<object, object?>
                    {
                        ["roleId"] = "{{var:roleId}}"
                    },
                    Tests =
                    [
                        new TestDefinition
                        {
                            Name = "Returns data",
                            ExpectedStatus = 200
                        }
                    ]
                }
            ]
        };

        var provider = new StubConfiguredTestSuiteProvider(new LoadedTestSuite(
            new ApiTestSuiteDefinition
            {
                Environments = [environment]
            },
            [environmentFilePath, endpointFilePath]));

        var service = CreateService(provider);
        var endpoint = environment.Endpoints[0];

        var seed = await service.GetEditorSeedAsync(
            DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
            DashboardSuiteManifestFactory.CreateEndpointId(environment, endpoint));

        Assert.Contains("UserId=233083", seed.CurlCommand);
        Assert.Contains("--header \"X-Role: 106\"", seed.CurlCommand);
        Assert.Contains("\\\"roleId\\\":\\\"106\\\"", seed.CurlCommand);
        Assert.DoesNotContain("{{var:userId}}", seed.CurlCommand);
        Assert.DoesNotContain("{{var:roleId}}", seed.CurlCommand);
    }

    [Fact]
    public async Task GetEditorSeedAsync_NormalizesPairListBodyShapeIntoJsonObject()
    {
        var environmentFilePath = Path.Combine(_tempDirectory, "pairlist-environment.yaml");
        var endpointFilePath = Path.Combine(_tempDirectory, "pairlist-endpoint.yaml");

        File.WriteAllText(environmentFilePath, """
            environments:
              - name: "Local"
                baseUrl: "https://api.example.com"
            """);

        File.WriteAllText(endpointFilePath, """
            targetEnvironments:
              - "Local"
            endpoints:
              - name: "Get Pair List"
                method: "POST"
                path: "/pair-list"
                body:
                  filters: []
                  ranges: []
                tests:
                  - name: "Returns data"
                    expectedStatus: 200
            """);

        var endpoint = new EndpointDefinition
        {
            Name = "Get Pair List",
            Method = "POST",
            Path = "/pair-list",
            Body = new object?[]
            {
                new object?[] { "filters", new List<object?>() },
                new object?[] { "ranges", new List<object?>() }
            },
            Tests =
            [
                new TestDefinition
                {
                    Name = "Returns data",
                    ExpectedStatus = 200
                }
            ]
        };

        var environment = new EnvironmentDefinition
        {
            Name = "Local",
            BaseUrl = "https://api.example.com",
            Endpoints = [endpoint]
        };

        var provider = new StubConfiguredTestSuiteProvider(new LoadedTestSuite(
            new ApiTestSuiteDefinition
            {
                Environments = [environment]
            },
            [environmentFilePath, endpointFilePath]));

        var service = CreateService(provider);

        var seed = await service.GetEditorSeedAsync(
            DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
            DashboardSuiteManifestFactory.CreateEndpointId(environment, endpoint));

        Assert.Contains("\\\"filters\\\":[]", seed.CurlCommand);
        Assert.Contains("\\\"ranges\\\":[]", seed.CurlCommand);
        Assert.DoesNotContain("System.Collections.Generic.List", seed.CurlCommand);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private (IConfiguredTestSuiteProvider Provider, EnvironmentDefinition Environment, EndpointDefinition Endpoint, string EnvironmentFilePath, string EndpointFilePath) CreateProvider()
    {
        var environmentFilePath = Path.Combine(_tempDirectory, "environment.yaml");
        var endpointFilePath = Path.Combine(_tempDirectory, "endpoint.yaml");

        File.WriteAllText(environmentFilePath, """
            environments:
              - name: "Local"
                baseUrl: "https://api.example.com"
            """);

        File.WriteAllText(endpointFilePath, """
            targetEnvironments:
              - "Local"
            endpoints:
              - name: "Get Accounts"
                method: "GET"
                path: "/accounts"
                tests:
                  - name: "Accounts should exist"
                    expectedStatus: 200
            """);

        var endpoint = new EndpointDefinition
        {
            Name = "Get Accounts",
            Method = "GET",
            Path = "/accounts",
            Tests =
            [
                new TestDefinition
                {
                    Name = "Accounts should exist",
                    ExpectedStatus = 200
                }
            ]
        };

        var environment = new EnvironmentDefinition
        {
            Name = "Local",
            BaseUrl = "https://api.example.com",
            Endpoints = [endpoint]
        };

        var provider = new StubConfiguredTestSuiteProvider(new LoadedTestSuite(
            new ApiTestSuiteDefinition
            {
                Environments = [environment]
            },
            [environmentFilePath, endpointFilePath]));

        return (provider, environment, endpoint, environmentFilePath, endpointFilePath);
    }

    private sealed class StubConfiguredTestSuiteProvider : IConfiguredTestSuiteProvider
    {
        private readonly LoadedTestSuite _loadedSuite;

        public StubConfiguredTestSuiteProvider(LoadedTestSuite loadedSuite)
        {
            _loadedSuite = loadedSuite;
        }

        public Task<LoadedTestSuite> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_loadedSuite);
        }
    }

    private static DashboardEndpointEditorService CreateService(IConfiguredTestSuiteProvider provider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var variableResolver = new VariableResolver(configuration, TimeProvider.System);
        return new DashboardEndpointEditorService(provider, variableResolver);
    }
}
