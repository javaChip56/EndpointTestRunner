using ApiTestRunner.Core.Models;

namespace ApiTestRunner.Core.Services;

public interface IVariableResolver
{
    object? ResolveValue(object? value, EnvironmentDefinition environment);

    string ResolveRequiredString(string? value, EnvironmentDefinition environment, string fieldName);
}
