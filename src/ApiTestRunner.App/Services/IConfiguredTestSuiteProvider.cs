using ApiTestRunner.App.Models;

namespace ApiTestRunner.App.Services;

public interface IConfiguredTestSuiteProvider
{
    Task<LoadedTestSuite> LoadAsync(CancellationToken cancellationToken = default);
}
