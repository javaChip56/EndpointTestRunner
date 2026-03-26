using ApiTestRunner.App.Services;
using ApiTestRunner.Core.Models;

namespace ApiTestRunner.App.Tests;

public sealed class DashboardSuiteManifestFactoryTests
{
    [Fact]
    public void Filter_KeepsOnlySelectedTestsAndRemovesEmptyEndpoints()
    {
        var suite = new ApiTestSuiteDefinition
        {
            Environments =
            [
                new EnvironmentDefinition
                {
                    Name = "Local",
                    BaseUrl = "https://localhost:5005",
                    Endpoints =
                    [
                        new EndpointDefinition
                        {
                            Name = "Endpoint A",
                            Method = "GET",
                            Path = "/api/a",
                            Tests =
                            [
                                new TestDefinition { Name = "A1", ExpectedStatus = 200 },
                                new TestDefinition { Name = "A2", ExpectedStatus = 200 }
                            ]
                        },
                        new EndpointDefinition
                        {
                            Name = "Endpoint B",
                            Method = "POST",
                            Path = "/api/b",
                            Tests =
                            [
                                new TestDefinition { Name = "B1", ExpectedStatus = 201 }
                            ]
                        }
                    ]
                }
            ]
        };

        var selectedTestId = DashboardSuiteManifestFactory.CreateTestId(
            suite.Environments[0],
            suite.Environments[0].Endpoints[0],
            suite.Environments[0].Endpoints[0].Tests[1],
            testIndex: 1);

        var filtered = DashboardSuiteManifestFactory.Filter(suite, [selectedTestId]);

        var environment = Assert.Single(filtered.Environments);
        var endpoint = Assert.Single(environment.Endpoints);
        Assert.Equal("Endpoint A", endpoint.Name);

        var test = Assert.Single(endpoint.Tests);
        Assert.Equal("A2", test.Name);
    }

    [Fact]
    public void CreateEditorSeed_BuildsCurlCommandAndExpandedTestDrafts()
    {
        var suite = new ApiTestSuiteDefinition
        {
            Environments =
            [
                new EnvironmentDefinition
                {
                    Name = "Local",
                    BaseUrl = "https://localhost:5005",
                    Endpoints =
                    [
                        new EndpointDefinition
                        {
                            Name = "Get Accounts",
                            Method = "POST",
                            Path = "/api/accounts",
                            Query = new Dictionary<string, object?>
                            {
                                ["customerId"] = "C1001"
                            },
                            Headers = new Dictionary<string, string>
                            {
                                ["Content-Type"] = "application/json"
                            },
                            Body = new Dictionary<string, object?>
                            {
                                ["page"] = 1
                            },
                            Tests =
                            [
                                new TestDefinition
                                {
                                    Name = "Accounts should exist",
                                    ExpectedStatus = 200,
                                    Assertions =
                                    [
                                        new AssertionDefinition
                                        {
                                            Field = "data.accounts",
                                            MinCount = 1
                                        },
                                        new AssertionDefinition
                                        {
                                            Field = "data.accounts",
                                            Contains = new Dictionary<string, object?>
                                            {
                                                ["status"] = "Active"
                                            }
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var environment = suite.Environments[0];
        var endpoint = environment.Endpoints[0];
        var seed = DashboardSuiteManifestFactory.CreateEditorSeed(
            suite,
            DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
            DashboardSuiteManifestFactory.CreateEndpointId(environment, endpoint));

        Assert.NotNull(seed);
        Assert.Contains("curl --request POST", seed.CurlCommand);
        Assert.Contains("https://localhost:5005/api/accounts?customerId=C1001", seed.CurlCommand);
        Assert.Contains("--header \"Content-Type: application/json\"", seed.CurlCommand);
        var test = Assert.Single(seed.Tests);
        Assert.Equal("Accounts should exist", test.Name);
        Assert.Equal(2, test.Assertions.Count);
        Assert.Contains(test.Assertions, assertion => assertion.Rule == "minCount");
        Assert.Contains(test.Assertions, assertion => assertion.Rule == "contains");
    }

    [Fact]
    public void CreateEditorSeed_SerializesEmptyArraysInBodyAsJsonArrays()
    {
        var suite = new ApiTestSuiteDefinition
        {
            Environments =
            [
                new EnvironmentDefinition
                {
                    Name = "Local",
                    BaseUrl = "https://localhost:5005",
                    Endpoints =
                    [
                        new EndpointDefinition
                        {
                            Name = "Get Summary",
                            Method = "POST",
                            Path = "/api/summary",
                            Body = new Dictionary<object, object?>
                            {
                                ["searches"] = new List<object?>(),
                                ["ranges"] = new List<object?>()
                            },
                            Tests =
                            [
                                new TestDefinition { Name = "Summary should load", ExpectedStatus = 200 }
                            ]
                        }
                    ]
                }
            ]
        };

        var environment = suite.Environments[0];
        var endpoint = environment.Endpoints[0];

        var seed = DashboardSuiteManifestFactory.CreateEditorSeed(
            suite,
            DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
            DashboardSuiteManifestFactory.CreateEndpointId(environment, endpoint));

        Assert.NotNull(seed);
        Assert.Contains("\\\"searches\\\":[]", seed.CurlCommand);
        Assert.Contains("\\\"ranges\\\":[]", seed.CurlCommand);
        Assert.DoesNotContain("System.Collections.Generic.List", seed.CurlCommand);
    }
}
