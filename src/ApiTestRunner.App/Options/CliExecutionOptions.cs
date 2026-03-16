namespace ApiTestRunner.App.Options;

public sealed class CliExecutionOptions
{
    public bool Enabled { get; init; }

    public IReadOnlyList<string> EnvironmentNames { get; init; } = [];

    public IReadOnlyList<string> TestFiles { get; init; } = [];

    public CliOutputFormat OutputFormat { get; init; } = CliOutputFormat.None;

    public string? OutputPath { get; init; }
}

public enum CliOutputFormat
{
    None,
    Json,
    JUnit
}
