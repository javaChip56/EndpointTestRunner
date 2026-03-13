using YamlDotNet.Serialization;

namespace ApiTestRunner.Core.Models;

public sealed class ApiTestSuiteDefinition
{
    public List<EnvironmentDefinition> Environments { get; init; } = [];
}

public sealed class EnvironmentDefinition
{
    public string Name { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public List<EndpointDefinition> Endpoints { get; init; } = [];
}

public sealed class EndpointDefinition
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

public sealed class TestDefinition
{
    public string Name { get; init; } = string.Empty;

    public int ExpectedStatus { get; init; }

    public List<AssertionDefinition> Assertions { get; init; } = [];
}

public sealed class AssertionDefinition
{
    public string Field { get; init; } = string.Empty;

    [YamlMember(Alias = "equals")]
    public object? EqualsValue { get; init; }

    public object? NotEquals { get; init; }

    public string? Type { get; init; }

    public string? ContainsText { get; init; }

    public string? StartsWith { get; init; }

    public string? EndsWith { get; init; }

    public bool? NotEmpty { get; init; }

    public int? MinCount { get; init; }

    public int? MaxCount { get; init; }

    public int? Count { get; init; }

    public Dictionary<string, object?> Contains { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
