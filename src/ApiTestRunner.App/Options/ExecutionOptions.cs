namespace ApiTestRunner.App.Options;

public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    public List<string> TestFiles { get; init; } = [];

    public int MaxConcurrency { get; init; } = 4;

    public int HttpTimeoutSeconds { get; init; } = 30;
}
