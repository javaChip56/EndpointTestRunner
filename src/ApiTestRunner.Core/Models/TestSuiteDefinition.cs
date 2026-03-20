using YamlDotNet.Serialization;

namespace ApiTestRunner.Core.Models;

public sealed record class ApiTestSuiteDefinition
{
    public List<EnvironmentDefinition> Environments { get; init; } = [];
}

public sealed record class ApiTestDocumentDefinition
{
    public List<EnvironmentDefinition> Environments { get; init; } = [];

    public List<EndpointDefinition> Endpoints { get; init; } = [];

    public List<string> TargetEnvironments { get; init; } = [];
}

public sealed record class EnvironmentDefinition
{
    public string Name { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public Dictionary<string, object?> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<EndpointDefinition> Endpoints { get; init; } = [];
}

public sealed record class EndpointDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Method { get; init; } = "GET";

    public string Path { get; init; } = string.Empty;

    public Dictionary<string, object?> PathParams { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, object?> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public object? Body { get; init; }

    public List<TestDefinition> Tests { get; init; } = [];
}

public sealed record class TestDefinition
{
    public string Name { get; init; } = string.Empty;

    public int ExpectedStatus { get; init; }

    public List<AssertionDefinition> Assertions { get; init; } = [];
}

public sealed record class AssertionDefinition
{
    public string Field { get; init; } = string.Empty;

    [YamlMember(Alias = "equals")]
    public object? EqualsValue { get; init; }

    public object? NotEquals { get; init; }

    public string? Type { get; init; }

    public string? ContainsText { get; init; }

    public string? StartsWith { get; init; }

    public string? EndsWith { get; init; }

    public object? NotEmpty { get; init; }

    public object? MinCount { get; init; }

    public object? MaxCount { get; init; }

    public object? Count { get; init; }

    public object? GreaterThan { get; init; }

    public object? GreaterThanOrEqual { get; init; }

    public object? LessThan { get; init; }

    public object? LessThanOrEqual { get; init; }

    public Dictionary<string, object?> Contains { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
