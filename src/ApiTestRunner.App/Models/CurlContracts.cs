namespace ApiTestRunner.App.Models;

public sealed class CurlAnalyzeRequest
{
    public string Command { get; init; } = string.Empty;

    public string? EnvironmentId { get; init; }

    public string? EndpointName { get; init; }

    public string? ResponseBody { get; init; }

    public IReadOnlyList<CurlTestDraft> Tests { get; init; } = [];

    public IReadOnlyList<CurlAssertionDraft> Assertions { get; init; } = [];
}

public sealed class CurlAnalyzeResponse
{
    public CurlRequestSummary? Request { get; init; }

    public CurlEnvironmentAnalysis Environment { get; init; } = new();

    public CurlEndpointAnalysis Endpoint { get; init; } = new();

    public CurlVariableAnalysis Variables { get; init; } = new();

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class CurlRequestSummary
{
    public string Method { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public object? Body { get; init; }

    public string? RawBody { get; init; }

    public string? RelativePath { get; init; }
}

public sealed class CurlAssertionDraft
{
    public string Field { get; init; } = string.Empty;

    public string Rule { get; init; } = string.Empty;

    public object? Value { get; init; }
}

public sealed class CurlTestDraft
{
    public string Name { get; init; } = string.Empty;

    public int ExpectedStatus { get; init; } = 200;

    public IReadOnlyList<CurlAssertionDraft> Assertions { get; init; } = [];
}

public sealed class CurlEnvironmentAnalysis
{
    public bool Exists { get; init; }

    public string MatchStatus { get; init; } = "new";

    public string SuggestedName { get; init; } = string.Empty;

    public IReadOnlyList<string> MatchedEnvironmentNames { get; init; } = [];

    public IReadOnlyList<CurlYamlPreview> MatchedYamlPreviews { get; init; } = [];

    public string? SuggestedFilePath { get; init; }

    public string? CurrentYaml { get; init; }

    public string? SuggestedYaml { get; init; }

    public string? DiffYaml { get; init; }

    public IReadOnlyList<CurlEnvironmentCandidate> Candidates { get; init; } = [];
}

public sealed class CurlEndpointAnalysis
{
    public bool Exists { get; init; }

    public string MatchStatus { get; init; } = "new";

    public string SuggestedName { get; init; } = string.Empty;

    public IReadOnlyList<string> MatchedEnvironmentNames { get; init; } = [];

    public IReadOnlyList<CurlYamlPreview> MatchedYamlPreviews { get; init; } = [];

    public string? GeneratedYaml { get; init; }

    public string? SuggestedFilePath { get; init; }

    public string? CurrentYaml { get; init; }

    public string? SuggestedYaml { get; init; }

    public string? DiffYaml { get; init; }

    public IReadOnlyList<CurlEndpointCandidate> Candidates { get; init; } = [];
}

public sealed class CurlYamlPreview
{
    public string Title { get; init; } = string.Empty;

    public string Yaml { get; init; } = string.Empty;
}

public sealed class CurlVariableAnalysis
{
    public bool HasSuggestions { get; init; }

    public IReadOnlyList<string> VariableNames { get; init; } = [];

    public string? SuggestedYaml { get; init; }

    public bool IncludedInEnvironmentYaml { get; init; }
}

public sealed class CurlEnvironmentCandidate
{
    public string Name { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;
}

public sealed class CurlEndpointCandidate
{
    public string Name { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public IReadOnlyList<string> EnvironmentNames { get; init; } = [];
}
