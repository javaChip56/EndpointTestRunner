using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ApiTestRunner.App.Options;
using ApiTestRunner.Core.Models;
using Microsoft.Extensions.Options;

namespace ApiTestRunner.App.Services;

public sealed class CliResultWriter
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptions<CliExecutionOptions> _cliExecutionOptions;

    public CliResultWriter(
        IHostEnvironment hostEnvironment,
        IOptions<CliExecutionOptions> cliExecutionOptions)
    {
        _hostEnvironment = hostEnvironment;
        _cliExecutionOptions = cliExecutionOptions;
    }

    public async Task<string?> WriteAsync(TestRunResult result, CancellationToken cancellationToken = default)
    {
        var options = _cliExecutionOptions.Value;
        if (options.OutputFormat == CliOutputFormat.None)
        {
            return null;
        }

        var payload = options.OutputFormat switch
        {
            CliOutputFormat.Json => JsonSerializer.Serialize(result, JsonSerializerOptions),
            CliOutputFormat.JUnit => BuildJUnitDocument(result).ToString(),
            _ => throw new InvalidOperationException($"Unsupported output format '{options.OutputFormat}'.")
        };

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            await Console.Out.WriteLineAsync(payload);
            return null;
        }

        var outputPath = ResolvePath(options.OutputPath);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, payload, Encoding.UTF8, cancellationToken);
        return outputPath;
    }

    private string ResolvePath(string configuredPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, configuredPath));
    }

    private static XDocument BuildJUnitDocument(TestRunResult result)
    {
        var suiteElement = new XElement(
            "testsuite",
            new XAttribute("name", "ApiTestRunner"),
            new XAttribute("tests", result.TotalTests),
            new XAttribute("failures", result.FailedTests),
            new XAttribute("errors", 0),
            new XAttribute("time", (result.TotalDurationMs / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));

        foreach (var environment in result.Environments)
        {
            foreach (var endpoint in environment.Endpoints)
            {
                foreach (var test in endpoint.Tests)
                {
                    var testCaseElement = new XElement(
                        "testcase",
                        new XAttribute("classname", $"{environment.Name}.{endpoint.Name}"),
                        new XAttribute("name", test.Name),
                        new XAttribute("time", (endpoint.DurationMs / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));

                    if (!test.IsSuccess)
                    {
                        testCaseElement.Add(new XElement(
                            "failure",
                            new XAttribute("message", test.ErrorMessage ?? "Test failed"),
                            test.ErrorMessage ?? "Test failed"));
                    }

                    suiteElement.Add(testCaseElement);
                }
            }
        }

        return new XDocument(new XElement("testsuites", suiteElement));
    }
}
