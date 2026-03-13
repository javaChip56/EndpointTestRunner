using ApiTestRunner.App.Options;
using ApiTestRunner.Core.Models;
using ApiTestRunner.Core.Services;
using Microsoft.Extensions.Options;

namespace ApiTestRunner.App.Services;

public sealed class TestRunCoordinator
{
    private readonly IYamlTestSuiteLoader _loader;
    private readonly IApiTestExecutor _executor;
    private readonly IOptions<ExecutionOptions> _executionOptions;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<TestRunCoordinator> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private DashboardState _currentState = DashboardState.NotStarted();

    public TestRunCoordinator(
        IYamlTestSuiteLoader loader,
        IApiTestExecutor executor,
        IOptions<ExecutionOptions> executionOptions,
        IHostEnvironment hostEnvironment,
        ILogger<TestRunCoordinator> logger)
    {
        _loader = loader;
        _executor = executor;
        _executionOptions = executionOptions;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public DashboardState GetState()
    {
        return _currentState;
    }

    public async Task<DashboardState> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _runLock.WaitAsync(cancellationToken);

        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            _currentState = _currentState with
            {
                IsRunning = true,
                LastStartedAtUtc = startedAt,
                LastError = null
            };

            var filePaths = ResolveConfiguredFiles(_executionOptions.Value.TestFiles);
            var suite = await _loader.LoadAsync(filePaths, cancellationToken);
            var result = await _executor.RunAsync(suite, _executionOptions.Value.MaxConcurrency, cancellationToken);

            _currentState = _currentState with
            {
                IsRunning = false,
                LastStartedAtUtc = startedAt,
                LastCompletedAtUtc = result.CompletedAtUtc,
                LastRun = result,
                LastError = null
            };

            return _currentState;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Test run failed");

            _currentState = _currentState with
            {
                IsRunning = false,
                LastCompletedAtUtc = DateTimeOffset.UtcNow,
                LastError = exception.Message
            };

            return _currentState;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private IReadOnlyList<string> ResolveConfiguredFiles(IEnumerable<string> configuredFiles)
    {
        var resolvedFiles = configuredFiles
            .Select(file => Path.IsPathRooted(file) ? file : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, file)))
            .ToArray();

        if (resolvedFiles.Length == 0)
        {
            throw new InvalidOperationException("Execution:TestFiles must contain at least one YAML file.");
        }

        return resolvedFiles;
    }
}

public sealed record DashboardState(
    bool IsRunning,
    DateTimeOffset? LastStartedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    TestRunResult? LastRun,
    string? LastError)
{
    public static DashboardState NotStarted() => new(false, null, null, null, null);
}
