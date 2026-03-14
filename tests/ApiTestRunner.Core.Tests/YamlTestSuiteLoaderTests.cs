using ApiTestRunner.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiTestRunner.Core.Tests;

public sealed class YamlTestSuiteLoaderTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly YamlTestSuiteLoader _loader;

    public YamlTestSuiteLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiTestRunnerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _loader = new YamlTestSuiteLoader(NullLogger<YamlTestSuiteLoader>.Instance);
    }

    [Fact]
    public async Task LoadAsync_MergesSharedEnvironmentFileWithEndpointOnlyFiles()
    {
        var environmentFile = WriteYaml("environments.yaml", """
            environments:
              - name: Local
                baseUrl: https://localhost:7001
                variables:
                  reportMonth: "{{now:MM}}"
            """);

        var endpointFile = WriteYaml("accounts.yaml", """
            targetEnvironments:
              - Local

            endpoints:
              - name: Get Accounts
                method: GET
                path: /api/accounts
                tests:
                  - name: Accounts should exist
                    expectedStatus: 200
            """);

        var suite = await _loader.LoadAsync([environmentFile, endpointFile]);

        var environment = Assert.Single(suite.Environments);
        Assert.Equal("Local", environment.Name);
        Assert.Equal("https://localhost:7001", environment.BaseUrl);
        Assert.Equal("{{now:MM}}", environment.Variables["reportMonth"]);

        var endpoint = Assert.Single(environment.Endpoints);
        Assert.Equal("Get Accounts", endpoint.Name);
        Assert.Equal("GET", endpoint.Method);
    }

    [Fact]
    public async Task LoadAsync_AttachesEndpointOnlyFileToSingleEnvironmentWhenTargetsOmitted()
    {
        var environmentFile = WriteYaml("single-environment.yaml", """
            environments:
              - name: Local
                baseUrl: https://localhost:7001
            """);

        var endpointFile = WriteYaml("login.yaml", """
            endpoints:
              - name: Login
                method: POST
                path: /api/auth/login
                tests:
                  - name: Login should succeed
                    expectedStatus: 200
                    assertions:
                      - field: success
                        equals: true
            """);

        var suite = await _loader.LoadAsync([environmentFile, endpointFile]);

        var environment = Assert.Single(suite.Environments);
        var endpoint = Assert.Single(environment.Endpoints);
        Assert.Equal("Login", endpoint.Name);
        Assert.True(endpoint.Tests[0].Assertions[0].EqualsValue is bool boolean && boolean);
    }

    [Fact]
    public async Task LoadAsync_ThrowsForConflictingEnvironmentBaseUrls()
    {
        var firstFile = WriteYaml("local-a.yaml", """
            environments:
              - name: Local
                baseUrl: https://localhost:7001
            """);

        var secondFile = WriteYaml("local-b.yaml", """
            environments:
              - name: Local
                baseUrl: https://localhost:7002
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _loader.LoadAsync([firstFile, secondFile]));

        Assert.Contains("conflicts with an existing baseUrl", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string WriteYaml(string fileName, string contents)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
