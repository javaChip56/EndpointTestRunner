using ApiTestRunner.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ApiTestRunner.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiTestRunnerCore(this IServiceCollection services)
    {
        services.AddSingleton<IYamlTestSuiteLoader, YamlTestSuiteLoader>();
        services.AddSingleton<IAssertionEvaluator, AssertionEvaluator>();

        return services;
    }
}
