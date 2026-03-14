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
}
