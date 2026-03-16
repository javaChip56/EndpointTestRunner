using ApiTestRunner.App.Options;
using ApiTestRunner.App.Models;
using ApiTestRunner.Core.Models;
using ApiTestRunner.Core.Services;
using Microsoft.Extensions.Options;

namespace ApiTestRunner.App.Services;

public sealed class CliExecutionHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IConfiguredTestSuiteProvider _suiteProvider;
    private readonly IApiTestExecutor _executor;
    private readonly IOptions<ExecutionOptions> _executionOptions;
    private readonly IOptions<CliExecutionOptions> _cliExecutionOptions;
    private readonly CliResultWriter _resultWriter;
    private readonly ILogger<CliExecutionHostedService> _logger;

    public CliExecutionHostedService(
        IHostApplicationLifetime applicationLifetime,
        IConfiguredTestSuiteProvider suiteProvider,
        IApiTestExecutor executor,
        IOptions<ExecutionOptions> executionOptions,
        IOptions<CliExecutionOptions> cliExecutionOptions,
        CliResultWriter resultWriter,
        ILogger<CliExecutionHostedService> logger)
    {
        _applicationLifetime = applicationLifetime;
        _suiteProvider = suiteProvider;
        _executor = executor;
        _executionOptions = executionOptions;
        _cliExecutionOptions = cliExecutionOptions;
        _resultWriter = resultWriter;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _applicationLifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var loadedSuite = await _suiteProvider.LoadAsync(CancellationToken.None);
                    var executionSuite = FilterSuiteByEnvironmentNames(
                        loadedSuite.Suite,
                        _cliExecutionOptions.Value.EnvironmentNames);
                    var result = await _executor.RunAsync(
                        executionSuite,
                        _executionOptions.Value.MaxConcurrency,
                        CancellationToken.None);

                    WriteSummary(result, loadedSuite.FilePaths);
                    var outputPath = await _resultWriter.WriteAsync(result, CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(outputPath))
                    {
                        await Console.Out.WriteLineAsync($"Wrote {_cliExecutionOptions.Value.OutputFormat} results to {outputPath}");
                    }

                    Environment.ExitCode = result.IsSuccess ? 0 : 1;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "CLI execution failed");
                    await Console.Error.WriteLineAsync(exception.Message);
                    Environment.ExitCode = 1;
                }
                finally
                {
                    _applicationLifetime.StopApplication();
                }
            });
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public static ApiTestSuiteDefinition FilterSuiteByEnvironmentNames(
        ApiTestSuiteDefinition suite,
        IReadOnlyList<string> environmentNames)
    {
        if (environmentNames.Count == 0)
        {
            return suite;
        }

        var names = new HashSet<string>(environmentNames, StringComparer.OrdinalIgnoreCase);
        var filteredEnvironments = suite.Environments
            .Where(environment => names.Contains(environment.Name))
            .ToList();

        if (filteredEnvironments.Count == 0)
        {
            throw new InvalidOperationException(
                $"None of the requested environments were found: {string.Join(", ", environmentNames)}");
        }

        return new ApiTestSuiteDefinition
        {
            Environments = filteredEnvironments
        };
    }

    private static void WriteSummary(TestRunResult result, IReadOnlyList<string> filePaths)
    {
        Console.WriteLine($"Loaded {filePaths.Count} YAML files.");
        Console.WriteLine($"Executed {result.TotalTests} tests across {result.TotalEndpoints} endpoints.");
        Console.WriteLine($"Passed: {result.PassedTests}");
        Console.WriteLine($"Failed: {result.FailedTests}");
        Console.WriteLine($"Duration: {Math.Round(result.TotalDurationMs)} ms");
    }
}
