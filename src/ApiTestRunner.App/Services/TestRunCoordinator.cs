using ApiTestRunner.App.Models;
using ApiTestRunner.App.Options;
using ApiTestRunner.Core.Models;
using ApiTestRunner.Core.Services;
using Microsoft.Extensions.Options;

namespace ApiTestRunner.App.Services;

public sealed class TestRunCoordinator
{
    private readonly IConfiguredTestSuiteProvider _suiteProvider;
    private readonly DashboardEndpointEditorService _endpointEditorService;
    private readonly IApiTestExecutor _executor;
    private readonly IOptions<ExecutionOptions> _executionOptions;
    private readonly ILogger<TestRunCoordinator> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private DashboardState _currentState = DashboardState.NotStarted();

    public TestRunCoordinator(
        IConfiguredTestSuiteProvider suiteProvider,
        DashboardEndpointEditorService endpointEditorService,
        IApiTestExecutor executor,
        IOptions<ExecutionOptions> executionOptions,
        ILogger<TestRunCoordinator> logger)
    {
        _suiteProvider = suiteProvider;
        _endpointEditorService = endpointEditorService;
        _executor = executor;
        _executionOptions = executionOptions;
        _logger = logger;
    }

    public DashboardState GetState()
    {
        return _currentState;
    }

    public async Task<DashboardSuiteManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        var loadedSuite = await _suiteProvider.LoadAsync(cancellationToken);
        return DashboardSuiteManifestFactory.Create(loadedSuite.Suite);
    }

    public async Task<DashboardEndpointEditorSeed> GetEditorSeedAsync(
        string environmentId,
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        return await _endpointEditorService.GetEditorSeedAsync(environmentId, endpointId, cancellationToken);
    }

    public async Task<DashboardEndpointSaveResponse> SaveEditorAsync(
        DashboardEndpointSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _endpointEditorService.SaveAsync(request, cancellationToken);
    }

    public async Task<DashboardState> ExecuteAsync(
        TestSelectionRequest? selectionRequest,
        CancellationToken cancellationToken = default)
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

            var loadedSuite = await _suiteProvider.LoadAsync(cancellationToken);
            var suite = FilterSuiteForExecution(loadedSuite.Suite, selectionRequest);
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

    private static ApiTestRunner.Core.Models.ApiTestSuiteDefinition FilterSuiteForExecution(
        ApiTestRunner.Core.Models.ApiTestSuiteDefinition suite,
        TestSelectionRequest? selectionRequest)
    {
        if (selectionRequest is null || selectionRequest.RunAll)
        {
            return suite;
        }

        if (selectionRequest.SelectedTestIds.Count == 0)
        {
            throw new InvalidOperationException("Select at least one test before running a filtered suite.");
        }

        var filteredSuite = DashboardSuiteManifestFactory.Filter(suite, selectionRequest.SelectedTestIds);

        if (filteredSuite.Environments.Count == 0)
        {
            throw new InvalidOperationException("The selected tests were not found in the loaded YAML suite.");
        }

        return filteredSuite;
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
