using ApiTestRunner.App.Models;
using ApiTestRunner.App.Services;
using ApiTestRunner.Core.Models;

namespace ApiTestRunner.App.Tests;

public sealed class CurlCommandAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_DetectsExistingEndpointUsingPathTemplate()
    {
        var analyzer = new CurlCommandAnalyzer(new StubConfiguredTestSuiteProvider(new ApiTestSuiteDefinition
        {
            Environments =
            [
                new EnvironmentDefinition
                {
                    Name = "Uat",
                    BaseUrl = "https://api.example.com",
                    Endpoints =
                    [
                        new EndpointDefinition
                        {
                            Name = "Get Customer Details",
                            Method = "GET",
                            Path = "/customers/{customerId}",
                            Tests =
                            [
                                new TestDefinition { Name = "Customer lookup should succeed", ExpectedStatus = 200 }
                            ]
                        }
                    ]
                }
            ]
        }));

        var result = await analyzer.AnalyzeAsync(new CurlAnalyzeRequest
        {
            Command = "curl --request GET \"https://api.example.com/customers/C1001\""
        });

        Assert.NotNull(result.Request);
        Assert.True(result.Environment.Exists);
        Assert.True(result.Endpoint.Exists);
        Assert.Contains("Uat", result.Endpoint.MatchedEnvironmentNames);
        Assert.Null(result.Endpoint.SuggestedYaml);
    }

    [Fact]
    public async Task AnalyzeAsync_GeneratesEnvironmentAndEndpointYamlWhenMissing()
    {
        var analyzer = new CurlCommandAnalyzer(new StubConfiguredTestSuiteProvider(new ApiTestSuiteDefinition
        {
            Environments =
            [
                new EnvironmentDefinition
                {
                    Name = "Local",
                    BaseUrl = "https://localhost:5005"
                }
            ]
        }));

        var result = await analyzer.AnalyzeAsync(new CurlAnalyzeRequest
        {
            Command = """
                curl --request POST "https://api.partner.com/AccountHoldingsMgmt/GetAccountList?baseCurrency=SGD" \
                  --header "Content-Type: application/json" \
                  --data "{\"currentPageNumber\":1,\"recordsPerPage\":10}"
                """
        });

        Assert.False(result.Environment.Exists);
        Assert.False(result.Endpoint.Exists);
        Assert.NotNull(result.Environment.SuggestedYaml);
        Assert.Contains("baseUrl: https://api.partner.com", result.Environment.SuggestedYaml);
        Assert.NotNull(result.Endpoint.SuggestedYaml);
        Assert.Contains("path: /AccountHoldingsMgmt/GetAccountList", result.Endpoint.SuggestedYaml);
        Assert.Contains("baseCurrency: SGD", result.Endpoint.SuggestedYaml);
        Assert.Contains("currentPageNumber: 1", result.Endpoint.SuggestedYaml);
    }

    private sealed class StubConfiguredTestSuiteProvider : IConfiguredTestSuiteProvider
    {
        private readonly ApiTestSuiteDefinition _suite;

        public StubConfiguredTestSuiteProvider(ApiTestSuiteDefinition suite)
        {
            _suite = suite;
        }

        public Task<LoadedTestSuite> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LoadedTestSuite(_suite, []));
        }
    }
}
