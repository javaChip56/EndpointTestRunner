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
}
