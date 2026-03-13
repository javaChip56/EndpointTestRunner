using ApiTestRunner.Core.Models;

namespace ApiTestRunner.Core.Services;

public interface IApiTestExecutor
{
    Task<TestRunResult> RunAsync(
        ApiTestSuiteDefinition suite,
        int maxConcurrency,
        CancellationToken cancellationToken = default);
}
