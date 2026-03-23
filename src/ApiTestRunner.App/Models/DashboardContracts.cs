using ApiTestRunner.Core.Models;

namespace ApiTestRunner.App.Models;

public sealed class TestSelectionRequest
{
    public bool RunAll { get; init; } = true;

    public IReadOnlyList<string> SelectedTestIds { get; init; } = [];
}

public sealed class DashboardSuiteManifest
{
    public IReadOnlyList<DashboardEnvironmentManifest> Environments { get; init; } = [];

    public int TotalEndpoints => Environments.Sum(environment => environment.Endpoints.Count);

    public int TotalTests => Environments.Sum(environment => environment.TotalTests);
}

public sealed class DashboardEnvironmentManifest
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public IReadOnlyList<DashboardEndpointManifest> Endpoints { get; init; } = [];

    public int TotalTests => Endpoints.Sum(endpoint => endpoint.Tests.Count);
}

public sealed class DashboardEndpointManifest
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public IReadOnlyList<DashboardTestManifest> Tests { get; init; } = [];
}

public sealed class DashboardEndpointEditorSeed
{
    public string EnvironmentId { get; init; } = string.Empty;

    public string EnvironmentName { get; init; } = string.Empty;

    public string EndpointId { get; init; } = string.Empty;

    public string CurlCommand { get; init; } = string.Empty;

    public IReadOnlyList<CurlTestDraft> Tests { get; init; } = [];
}

public sealed class DashboardTestManifest
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int ExpectedStatus { get; init; }
}

public sealed record LoadedTestSuite(
    ApiTestSuiteDefinition Suite,
    IReadOnlyList<string> FilePaths);
