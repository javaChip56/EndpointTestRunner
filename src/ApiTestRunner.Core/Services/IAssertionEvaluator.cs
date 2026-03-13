using System.Text.Json.Nodes;
using ApiTestRunner.Core.Models;

namespace ApiTestRunner.Core.Services;

public interface IAssertionEvaluator
{
    IReadOnlyList<AssertionResult> EvaluateAll(
        IReadOnlyList<AssertionDefinition> assertions,
        JsonNode? responseJson);
}
