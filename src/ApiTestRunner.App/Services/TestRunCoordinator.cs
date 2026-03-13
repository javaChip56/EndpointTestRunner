using System.Text.RegularExpressions;
using ApiTestRunner.App.Options;
using ApiTestRunner.Core.Models;
using ApiTestRunner.Core.Services;
using Microsoft.Extensions.Options;

namespace ApiTestRunner.App.Services;

public sealed class TestRunCoordinator
{
    private static readonly char[] WildcardCharacters = ['*', '?'];

    private readonly IYamlTestSuiteLoader _loader;
    private readonly IApiTestExecutor _executor;
    private readonly IOptions<ExecutionOptions> _executionOptions;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<TestRunCoordinator> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private DashboardState _currentState = DashboardState.NotStarted();

    public TestRunCoordinator(
        IYamlTestSuiteLoader loader,
        IApiTestExecutor executor,
        IOptions<ExecutionOptions> executionOptions,
        IHostEnvironment hostEnvironment,
        ILogger<TestRunCoordinator> logger)
    {
        _loader = loader;
        _executor = executor;
        _executionOptions = executionOptions;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public DashboardState GetState()
    {
        return _currentState;
    }

    public async Task<DashboardState> ExecuteAsync(CancellationToken cancellationToken = default)
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

            var filePaths = ResolveConfiguredFiles(_executionOptions.Value.TestFiles);
            var suite = await _loader.LoadAsync(filePaths, cancellationToken);
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

    private IReadOnlyList<string> ResolveConfiguredFiles(IEnumerable<string> configuredEntries)
    {
        var expandedFiles = new List<string>();

        foreach (var entry in configuredEntries.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (ContainsWildcard(entry))
            {
                expandedFiles.AddRange(ResolveGlob(entry));
                continue;
            }

            var resolvedPath = ResolvePath(entry);

            if (Directory.Exists(resolvedPath))
            {
                expandedFiles.AddRange(Directory
                    .EnumerateFiles(resolvedPath, "*.yaml", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(resolvedPath, "*.yml", SearchOption.AllDirectories)));
                continue;
            }

            expandedFiles.Add(resolvedPath);
        }

        var files = expandedFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException("Execution:TestFiles must contain at least one YAML file or matching glob pattern.");
        }

        return files;
    }

    private IReadOnlyList<string> ResolveGlob(string pattern)
    {
        var fullPattern = ResolvePath(pattern);
        var searchRoot = GetSearchRoot(fullPattern);

        if (!Directory.Exists(searchRoot))
        {
            throw new DirectoryNotFoundException(
                $"The directory portion of glob pattern '{pattern}' does not exist: '{searchRoot}'.");
        }

        var relativePattern = Path.GetRelativePath(searchRoot, fullPattern)
            .Replace('\\', '/');

        var regex = BuildGlobRegex(relativePattern);
        var matches = Directory
            .EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relativePath = Path.GetRelativePath(searchRoot, path).Replace('\\', '/');
                return regex.IsMatch(relativePath);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"Glob pattern '{pattern}' did not match any files.");
        }

        return matches;
    }

    private string ResolvePath(string configuredPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, configuredPath));
    }

    private static bool ContainsWildcard(string value)
    {
        return value.IndexOfAny(WildcardCharacters) >= 0;
    }

    private static string GetSearchRoot(string fullPattern)
    {
        var normalizedPattern = fullPattern.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(normalizedPattern) ?? string.Empty;
        var remainder = normalizedPattern[root.Length..];
        var segments = remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = string.IsNullOrEmpty(root) ? string.Empty : root;

        foreach (var segment in segments)
        {
            if (segment.Contains('*') || segment.Contains('?'))
            {
                break;
            }

            current = string.IsNullOrEmpty(current)
                ? segment
                : Path.Combine(current, segment);
        }

        return string.IsNullOrEmpty(current) ? Directory.GetCurrentDirectory() : current;
    }

    private static Regex BuildGlobRegex(string relativePattern)
    {
        var pattern = relativePattern.Replace('\\', '/');
        var regexPattern = "^";

        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];

            if (character == '*')
            {
                var hasDoubleStar = index + 1 < pattern.Length && pattern[index + 1] == '*';
                if (hasDoubleStar)
                {
                    var followedByDirectorySeparator = index + 2 < pattern.Length && pattern[index + 2] == '/';
                    regexPattern += followedByDirectorySeparator ? @"(?:.*/)?" : @".*";
                    index += followedByDirectorySeparator ? 2 : 1;
                    continue;
                }

                regexPattern += @"[^/]*";
                continue;
            }

            if (character == '?')
            {
                regexPattern += @"[^/]";
                continue;
            }

            regexPattern += Regex.Escape(character.ToString());
        }

        regexPattern += "$";

        return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
