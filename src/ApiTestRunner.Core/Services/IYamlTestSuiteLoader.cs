using ApiTestRunner.Core.Models;

namespace ApiTestRunner.Core.Services;

public interface IYamlTestSuiteLoader
{
    Task<ApiTestSuiteDefinition> LoadAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
