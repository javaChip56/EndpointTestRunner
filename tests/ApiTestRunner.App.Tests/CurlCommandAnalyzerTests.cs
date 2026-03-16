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
        Assert.Equal("matched", result.Environment.MatchStatus);
        Assert.Equal("matched", result.Endpoint.MatchStatus);
        Assert.Contains("Uat", result.Endpoint.MatchedEnvironmentNames);
        Assert.NotNull(result.Environment.CurrentYaml);
        Assert.NotNull(result.Environment.SuggestedYaml);
        Assert.NotNull(result.Environment.DiffYaml);
        Assert.NotNull(result.Endpoint.CurrentYaml);
        Assert.NotNull(result.Endpoint.SuggestedYaml);
        Assert.NotNull(result.Endpoint.DiffYaml);
        Assert.Contains("name: \"Uat\"", result.Environment.SuggestedYaml);
        Assert.Contains("path: \"/customers/{customerId}\"", result.Endpoint.SuggestedYaml);
        Assert.Contains("name: \"Get Customer Details\"", result.Endpoint.SuggestedYaml);
        Assert.Contains("  - name: \"Uat\"", result.Environment.DiffYaml);
        Assert.Contains("+", result.Endpoint.DiffYaml);
        Assert.Contains("Get Customer Details should return success", result.Endpoint.DiffYaml);
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
        Assert.Equal("matched", result.Environment.MatchStatus);
        Assert.Contains("PartnerUat", result.Environment.MatchedEnvironmentNames);
        Assert.NotNull(result.Environment.CurrentYaml);
        Assert.NotNull(result.Environment.SuggestedYaml);
        Assert.NotNull(result.Environment.DiffYaml);
        Assert.Contains("name: \"PartnerUat\"", result.Environment.SuggestedYaml);
        Assert.Contains("baseUrl: \"https://api.partner.com/AccountHoldingsMgmt\"", result.Environment.SuggestedYaml);
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
        Assert.Equal("new", result.Environment.MatchStatus);
        Assert.Equal("new", result.Endpoint.MatchStatus);
        Assert.NotNull(result.Environment.SuggestedYaml);
        Assert.Contains("baseUrl: \"https://api.partner.com\"", result.Environment.SuggestedYaml);
        Assert.Contains("variables:", result.Environment.SuggestedYaml);
        Assert.Contains("baseCurrency: \"SGD\"", result.Environment.SuggestedYaml);
        Assert.NotNull(result.Endpoint.SuggestedYaml);
        Assert.Contains("path: \"/AccountHoldingsMgmt/GetAccountList\"", result.Endpoint.SuggestedYaml);
        Assert.Contains("baseCurrency: \"{{var:baseCurrency}}\"", result.Endpoint.SuggestedYaml);
        Assert.Contains("currentPageNumber: \"{{var:currentPageNumber}}\"", result.Endpoint.SuggestedYaml);
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
        Assert.Contains("field: \"statusCode\"", result.Endpoint.SuggestedYaml);
        Assert.Contains("equals: 1", result.Endpoint.SuggestedYaml);
        Assert.Contains("field: \"data.pagenationTemplate.dataLists\"", result.Endpoint.SuggestedYaml);
        Assert.Contains("minCount: 1", result.Endpoint.SuggestedYaml);
        Assert.Contains("notEmpty: true", result.Endpoint.SuggestedYaml);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsWarningsAndSuggestionsWhenYamlFilesAreMissing()
    {
        var analyzer = new CurlCommandAnalyzer(new ThrowingConfiguredTestSuiteProvider(
            new FileNotFoundException("Glob pattern '../../gwm4-api-dev/Endpoints/**/*.yaml' did not match any files.")));

        var result = await analyzer.AnalyzeAsync(new CurlAnalyzeRequest
        {
            Command = "curl --request POST \"https://api.partner.com/AccountHoldingsMgmt/GetAccountList\""
        });

        Assert.False(result.Environment.Exists);
        Assert.False(result.Endpoint.Exists);
        Assert.Equal("new", result.Environment.MatchStatus);
        Assert.Equal("new", result.Endpoint.MatchStatus);
        Assert.NotNull(result.Environment.SuggestedYaml);
        Assert.NotNull(result.Endpoint.SuggestedYaml);
    }

    [Fact]
    public async Task AnalyzeAsync_PreservesNumericLookingStringsInGeneratedYaml()
    {
        var analyzer = new CurlCommandAnalyzer(new StubConfiguredTestSuiteProvider(new ApiTestSuiteDefinition
        {
            Environments = []
        }));

        var result = await analyzer.AnalyzeAsync(new CurlAnalyzeRequest
        {
            Command = """
                curl --request POST "https://api.partner.com/AccountHoldingsMgmt/GetAccountList" \
                  --header "Content-Type: application/json" \
                  --data "{\"filters\":[{\"column\":\"userRoleID\",\"value\":\"106\",\"filterType\":\"equal\"}]}"
                """
        });

        Assert.NotNull(result.Endpoint.SuggestedYaml);
        Assert.NotNull(result.Variables.SuggestedYaml);
        Assert.Contains("userRoleID: \"106\"", result.Variables.SuggestedYaml);
        Assert.Contains("column: \"userRoleID\"", result.Endpoint.SuggestedYaml);
        Assert.Contains("value: \"{{var:userRoleID}}\"", result.Endpoint.SuggestedYaml);
        Assert.Contains("filterType: \"equal\"", result.Endpoint.SuggestedYaml);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsAmbiguousEnvironmentCandidatesWhenMultipleBaseUrlsMatch()
    {
        var analyzer = new CurlCommandAnalyzer(new StubConfiguredTestSuiteProvider(new ApiTestSuiteDefinition
        {
            Environments =
            [
                new EnvironmentDefinition
                {
                    Name = "PartnerUat",
                    BaseUrl = "https://api.partner.com"
                },
                new EnvironmentDefinition
                {
                    Name = "PartnerProd",
                    BaseUrl = "https://api.partner.com"
                }
            ]
        }));

        var result = await analyzer.AnalyzeAsync(new CurlAnalyzeRequest
        {
            Command = "curl --request GET \"https://api.partner.com/customers/C1001\""
        });

        Assert.True(result.Environment.Exists);
        Assert.Equal("ambiguous", result.Environment.MatchStatus);
        Assert.Null(result.Environment.CurrentYaml);
        Assert.Null(result.Environment.SuggestedYaml);
        Assert.Null(result.Environment.DiffYaml);
        Assert.Equal(2, result.Environment.Candidates.Count);
        Assert.Contains(result.Environment.Candidates, candidate => candidate.Name == "PartnerProd");
        Assert.Contains(result.Environment.Candidates, candidate => candidate.Name == "PartnerUat");
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsAmbiguousEndpointCandidatesWhenMultiplePathsMatch()
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
                            Tests = [ new TestDefinition { Name = "A", ExpectedStatus = 200 } ]
                        },
                        new EndpointDefinition
                        {
                            Name = "Get Customer Details Raw",
                            Method = "GET",
                            Path = "/customers/C1001",
                            Tests = [ new TestDefinition { Name = "B", ExpectedStatus = 200 } ]
                        }
                    ]
                }
            ]
        }));

        var result = await analyzer.AnalyzeAsync(new CurlAnalyzeRequest
        {
            Command = "curl --request GET \"https://api.example.com/customers/C1001\""
        });

        Assert.True(result.Endpoint.Exists);
        Assert.Equal("ambiguous", result.Endpoint.MatchStatus);
        Assert.Null(result.Endpoint.CurrentYaml);
        Assert.Null(result.Endpoint.SuggestedYaml);
        Assert.Null(result.Endpoint.DiffYaml);
        Assert.Equal(2, result.Endpoint.Candidates.Count);
        Assert.Contains(result.Endpoint.Candidates, candidate => candidate.Name == "Get Customer Details");
        Assert.Contains(result.Endpoint.Candidates, candidate => candidate.Name == "Get Customer Details Raw");
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

    private sealed class ThrowingConfiguredTestSuiteProvider : IConfiguredTestSuiteProvider
    {
        private readonly Exception _exception;

        public ThrowingConfiguredTestSuiteProvider(Exception exception)
        {
            _exception = exception;
        }

        public Task<LoadedTestSuite> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<LoadedTestSuite>(_exception);
        }
    }
}
