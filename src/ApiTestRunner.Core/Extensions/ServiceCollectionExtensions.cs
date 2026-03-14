using Microsoft.Extensions.DependencyInjection;
using ApiTestRunner.Core.Services;

namespace ApiTestRunner.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiTestRunnerCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IYamlTestSuiteLoader, YamlTestSuiteLoader>();
        services.AddSingleton<IAssertionEvaluator, AssertionEvaluator>();
        services.AddSingleton<IVariableResolver, VariableResolver>();

        return services;
    }
}
