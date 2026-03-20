using ApiTestRunner.App.Options;
using ApiTestRunner.App.Services;
using ApiTestRunner.Core.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ApiTestRunner.App.Tests;

public sealed class CliExecutionAndResultWriterTests : IDisposable
{
    private readonly string _tempDirectory;

    public CliExecutionAndResultWriterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ApiTestRunnerCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void FilterSuiteByEnvironmentNames_KeepsOnlyRequestedEnvironments()
    {
        var suite = new ApiTestSuiteDefinition
        {
            Environments =
            [
                new EnvironmentDefinition { Name = "Local", BaseUrl = "http://localhost:5005" },
                new EnvironmentDefinition { Name = "Uat", BaseUrl = "https://uat.example.com" }
            ]
        };

        var filtered = CliExecutionHostedService.FilterSuiteByEnvironmentNames(suite, ["Uat"]);

        var environment = Assert.Single(filtered.Environments);
        Assert.Equal("Uat", environment.Name);
    }

    [Fact]
    public async Task WriteAsync_WritesJUnitXmlToConfiguredOutputPath()
    {
        var outputPath = Path.Combine(_tempDirectory, "results.xml");
        var writer = new CliResultWriter(
            new StubHostEnvironment(_tempDirectory),
            Microsoft.Extensions.Options.Options.Create(new CliExecutionOptions
            {
                Enabled = true,
                OutputFormat = CliOutputFormat.JUnit,
                OutputPath = outputPath
            }));

        var result = new TestRunResult
        {
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1),
            Environments =
            [
                new EnvironmentRunResult
                {
                    Name = "Uat",
                    BaseUrl = "https://api.example.com",
                    Endpoints =
                    [
                        new EndpointRunResult
                        {
                            Name = "Get Customer",
                            Method = "GET",
                            RequestUrl = "https://api.example.com/customers/C1001",
                            DurationMs = 100,
                            Tests =
                            [
                                new TestCaseRunResult
                                {
                                    Name = "Customer should load",
                                    ExpectedStatus = 200,
                                    ActualStatus = 500,
                                    StatusMatched = false,
                                    IsSuccess = false,
                                    ErrorMessage = "Expected 200 but received 500"
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var writtenPath = await writer.WriteAsync(result);

        Assert.Equal(outputPath, writtenPath);
        var xml = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("<testsuite", xml);
        Assert.Contains("ApiTestRunner", xml);
        Assert.Contains("Customer should load", xml);
        Assert.Contains("failure", xml);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        }

        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "ApiTestRunner.App.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
