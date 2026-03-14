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
    public async Task AnalyzeAsync_DoesNotSuggestEnvironmentWhenExistingBaseUrlContainsPathPrefix()
    {
        var analyzer = new CurlCommandAnalyzer(new StubConfiguredTestSuiteProvider(new ApiTestSuiteDefinition
        {
            Environments =
            [
                new EnvironmentDefinition
                {
                    Name = "PartnerUat",
                    BaseUrl = "https://api.partner.com/AccountHoldingsMgmt"
                }
            ]
        }));

        var result = await analyzer.AnalyzeAsync(new CurlAnalyzeRequest
        {
            Command = "curl --request POST \"https://api.partner.com/AccountHoldingsMgmt/GetAccountList\""
        });

        Assert.True(result.Environment.Exists);
        Assert.Contains("PartnerUat", result.Environment.MatchedEnvironmentNames);
        Assert.Null(result.Environment.SuggestedYaml);
        Assert.Equal("/GetAccountList", result.Request?.RelativePath);
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

    [Fact]
    public async Task AnalyzeAsync_IncludesSelectedAssertionsInGeneratedYaml()
    {
        var analyzer = new CurlCommandAnalyzer(new StubConfiguredTestSuiteProvider(new ApiTestSuiteDefinition
        {
            Environments = []
        }));

        var result = await analyzer.AnalyzeAsync(new CurlAnalyzeRequest
        {
            Command = "curl --request POST \"https://api.partner.com/AccountHoldingsMgmt/GetAccountList\"",
            Assertions =
            [
                new CurlAssertionDraft
                {
                    Field = "statusCode",
                    Rule = "equals",
                    Value = 1
                },
                new CurlAssertionDraft
                {
                    Field = "data.pagenationTemplate.dataLists",
                    Rule = "minCount",
                    Value = 1
                },
                new CurlAssertionDraft
                {
                    Field = "data.pagenationTemplate.dataLists",
                    Rule = "notEmpty",
                    Value = true
                }
            ]
        });

        Assert.NotNull(result.Endpoint.SuggestedYaml);
        Assert.Contains("field: statusCode", result.Endpoint.SuggestedYaml);
        Assert.Contains("equals: 1", result.Endpoint.SuggestedYaml);
        Assert.Contains("field: data.pagenationTemplate.dataLists", result.Endpoint.SuggestedYaml);
        Assert.Contains("minCount: 1", result.Endpoint.SuggestedYaml);
        Assert.Contains("notEmpty: true", result.Endpoint.SuggestedYaml);
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
