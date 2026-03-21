using System.Text.Json.Nodes;
using ApiTestRunner.Core.Models;
using ApiTestRunner.Core.Services;

namespace ApiTestRunner.Core.Tests;

public sealed class AssertionEvaluatorTests
{
    private readonly AssertionEvaluator _evaluator = new();

    [Fact]
    public void EvaluateAll_ResolvesDotNotationAndArrayIndexes()
    {
        var response = JsonNode.Parse("""
            {
              "success": true,
              "data": {
                "accounts": [
                  {
                    "accountNo": "ACC-1001",
                    "status": "Active"
                  }
                ]
              }
            }
            """);

        var assertions = new[]
        {
            new AssertionDefinition { Field = "success", EqualsValue = true },
            new AssertionDefinition { Field = "data.accounts", Type = "array", MinCount = 1 },
            new AssertionDefinition { Field = "data.accounts[0].accountNo", StartsWith = "ACC-" },
            new AssertionDefinition
            {
                Field = "data.accounts",
                Contains = new Dictionary<string, object?> { ["status"] = "Active" }
            }
        };

        var results = _evaluator.EvaluateAll(assertions, response);

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Message));
    }

    [Fact]
    public void EvaluateAll_ReturnsFailedAssertionForInvalidFieldPath()
    {
        var response = JsonNode.Parse("""{ "data": { "items": [1, 2, 3] } }""");
        var assertions = new[]
        {
            new AssertionDefinition
            {
                Field = "data.items[abc]",
                Count = 3
            }
        };

        var results = _evaluator.EvaluateAll(assertions, response);

        var result = Assert.Single(results);
        Assert.False(result.IsSuccess);
        Assert.Equal("field", result.Rule);
        Assert.Contains("Invalid field path", result.Message);
    }

    [Fact]
    public void EvaluateAll_ResolvesPropertiesCaseInsensitively()
    {
        var response = JsonNode.Parse("""
            {
              "StatusCode": 1,
              "Data": {
                "PagenationTemplate": {
                  "DataLists": [1]
                }
              }
            }
            """);

        var assertions = new[]
        {
            new AssertionDefinition { Field = "statusCode", EqualsValue = 1 },
            new AssertionDefinition { Field = "data.pagenationTemplate.dataLists", MinCount = 1 }
        };

        var results = _evaluator.EvaluateAll(assertions, response);

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Message));
    }

    [Fact]
    public void EvaluateAll_AcceptsYamlSizedIntegralTypesForCountAssertions()
    {
        var response = JsonNode.Parse("""
            {
              "data": {
                "pagenationTemplate": {
                  "dataLists": [1, 2, 3]
                }
              }
            }
            """);

        var assertions = new[]
        {
            new AssertionDefinition
            {
                Field = "data.pagenationTemplate.dataLists",
                MinCount = (byte)1
            },
            new AssertionDefinition
            {
                Field = "data.pagenationTemplate.dataLists",
                MaxCount = (ushort)5
            },
            new AssertionDefinition
            {
                Field = "data.pagenationTemplate.dataLists",
                Count = (uint)3
            }
        };

        var results = _evaluator.EvaluateAll(assertions, response);

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Message));
    }

    [Fact]
    public void EvaluateAll_SupportsNumericComparisonAssertions()
    {
        var response = JsonNode.Parse("""
            {
              "statusCode": 1,
              "data": {
                "totalRowsCount": 149,
                "profitPercentage": -74.03
              }
            }
            """);

        var assertions = new[]
        {
            new AssertionDefinition { Field = "data.totalRowsCount", GreaterThan = 0 },
            new AssertionDefinition { Field = "data.totalRowsCount", GreaterThanOrEqual = 149 },
            new AssertionDefinition { Field = "data.totalRowsCount", LessThan = 200 },
            new AssertionDefinition { Field = "data.profitPercentage", LessThanOrEqual = -74.03m }
        };

        var results = _evaluator.EvaluateAll(assertions, response);

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Message));
    }

    [Fact]
    public void EvaluateAll_FailsNumericComparisonAssertionsWhenFieldIsNotNumeric()
    {
        var response = JsonNode.Parse("""{ "message": "Account list details found." }""");

        var assertions = new[]
        {
            new AssertionDefinition
            {
                Field = "message",
                GreaterThan = 0
            }
        };

        var results = _evaluator.EvaluateAll(assertions, response);

        var result = Assert.Single(results);
        Assert.False(result.IsSuccess);
        Assert.Equal("greaterThan", result.Rule);
        Assert.Contains("not a number", result.Message);
    }

    [Fact]
    public void EvaluateAll_SupportsNumericComparisonInsideContains()
    {
        var response = JsonNode.Parse("""
            {
              "data": {
                "accounts": [
                  {
                    "accountNo": "ACC-1001",
                    "portfolioValue": 3116.61,
                    "status": "Active"
                  },
                  {
                    "accountNo": "ACC-1002",
                    "portfolioValue": 0,
                    "status": "Inactive"
                  }
                ]
              }
            }
            """);

        var assertions = new[]
        {
            new AssertionDefinition
            {
                Field = "data.accounts",
                Contains = new Dictionary<string, object?>
                {
                    ["portfolioValue"] = new Dictionary<string, object?>
                    {
                        ["greaterThan"] = 1000
                    },
                    ["status"] = "Active"
                }
            }
        };

        var results = _evaluator.EvaluateAll(assertions, response);

        var result = Assert.Single(results);
        Assert.True(result.IsSuccess, result.Message);
    }

    [Fact]
    public void EvaluateAll_FailsContainsWhenNestedNumericComparisonDoesNotMatch()
    {
        var response = JsonNode.Parse("""
            {
              "data": {
                "accounts": [
                  {
                    "accountNo": "ACC-1001",
                    "portfolioValue": 3116.61,
                    "status": "Active"
                  }
                ]
              }
            }
            """);

        var assertions = new[]
        {
            new AssertionDefinition
            {
                Field = "data.accounts",
                Contains = new Dictionary<string, object?>
                {
                    ["portfolioValue"] = new Dictionary<string, object?>
                    {
                        ["lessThan"] = 1000
                    }
                }
            }
        };

        var results = _evaluator.EvaluateAll(assertions, response);

        var result = Assert.Single(results);
        Assert.False(result.IsSuccess);
        Assert.Equal("contains", result.Rule);
    }
}
