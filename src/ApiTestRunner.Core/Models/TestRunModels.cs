namespace ApiTestRunner.Core.Models;

public sealed class TestRunResult
{
    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public IReadOnlyList<EnvironmentRunResult> Environments { get; init; } = [];

    public int TotalEndpoints => Environments.Sum(environment => environment.Endpoints.Count);

    public int TotalTests => Environments.Sum(environment => environment.TotalTests);

    public int PassedTests => Environments.Sum(environment => environment.PassedTests);

    public int FailedTests => Environments.Sum(environment => environment.FailedTests);

    public int PassedEndpoints => Environments.Sum(environment => environment.PassedEndpoints);

    public int FailedEndpoints => Environments.Sum(environment => environment.FailedEndpoints);

    public bool IsSuccess => FailedTests == 0;

    public double TotalDurationMs => (CompletedAtUtc - StartedAtUtc).TotalMilliseconds;
}

public sealed class EnvironmentRunResult
{
    public string Name { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public IReadOnlyList<EndpointRunResult> Endpoints { get; init; } = [];

    public int TotalTests => Endpoints.Sum(endpoint => endpoint.Tests.Count);

    public int PassedTests => Endpoints.Sum(endpoint => endpoint.Tests.Count(test => test.IsSuccess));

    public int FailedTests => TotalTests - PassedTests;

    public int PassedEndpoints => Endpoints.Count(endpoint => endpoint.IsSuccess);

    public int FailedEndpoints => Endpoints.Count - PassedEndpoints;
}

public sealed class EndpointRunResult
{
    public string Name { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public string RequestUrl { get; init; } = string.Empty;

    public double DurationMs { get; init; }

    public bool IsSuccess => Tests.All(test => test.IsSuccess);

    public int? ActualStatus { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ResponseBody { get; init; }

    public IReadOnlyList<TestCaseRunResult> Tests { get; init; } = [];
}

public sealed class TestCaseRunResult
{
    public string Name { get; init; } = string.Empty;

    public int ExpectedStatus { get; init; }

    public int? ActualStatus { get; init; }

    public bool StatusMatched { get; init; }

    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<AssertionResult> Assertions { get; init; } = [];
}

public sealed class AssertionResult
{
    public string Field { get; init; } = string.Empty;

    public string Rule { get; init; } = string.Empty;

    public bool IsSuccess { get; init; }

    public string Message { get; init; } = string.Empty;
}
