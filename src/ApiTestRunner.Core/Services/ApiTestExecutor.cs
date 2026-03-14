using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiTestRunner.Core.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace ApiTestRunner.Core.Services;

public sealed class ApiTestExecutor : IApiTestExecutor
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethod.Get.Method,
        HttpMethod.Post.Method,
        HttpMethod.Put.Method,
        HttpMethod.Patch.Method,
        HttpMethod.Delete.Method
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly IAssertionEvaluator _assertionEvaluator;
    private readonly IVariableResolver _variableResolver;
    private readonly ILogger<ApiTestExecutor> _logger;

    public ApiTestExecutor(
        HttpClient httpClient,
        IAssertionEvaluator assertionEvaluator,
        IVariableResolver variableResolver,
        ILogger<ApiTestExecutor> logger)
    {
        _httpClient = httpClient;
        _assertionEvaluator = assertionEvaluator;
        _variableResolver = variableResolver;
        _logger = logger;
    }

    public async Task<TestRunResult> RunAsync(
        ApiTestSuiteDefinition suite,
        int maxConcurrency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suite);

        var startedAt = DateTimeOffset.UtcNow;
        var throttle = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        try
        {
            var environmentTasks = suite.Environments.Select(environment =>
                ExecuteEnvironmentAsync(environment, throttle, cancellationToken));

            var environments = await Task.WhenAll(environmentTasks);

            return new TestRunResult
            {
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Environments = environments
            };
        }
        finally
        {
            throttle.Dispose();
        }
    }

    private async Task<EnvironmentRunResult> ExecuteEnvironmentAsync(
        EnvironmentDefinition environment,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        var endpointTasks = environment.Endpoints.Select(async endpoint =>
        {
            await throttle.WaitAsync(cancellationToken);

            try
            {
                return await ExecuteEndpointAsync(environment, endpoint, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        });

        var endpoints = await Task.WhenAll(endpointTasks);

        return new EnvironmentRunResult
        {
            Name = environment.Name,
            BaseUrl = environment.BaseUrl,
            Endpoints = endpoints.OrderBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private async Task<EndpointRunResult> ExecuteEndpointAsync(
        EnvironmentDefinition environment,
        EndpointDefinition endpoint,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestUrl = string.Empty;
        string? requestBody = null;
        string? responseBody = null;
        int? actualStatus = null;

        try
        {
            using var request = BuildRequest(environment, endpoint, out requestUrl, out requestBody);

            _logger.LogInformation("Executing {Method} {Url}", request.Method.Method, requestUrl);
            _logger.LogDebug(
                "Request details for {Method} {Url}. Headers: {Headers}. Body: {Body}",
                request.Method.Method,
                requestUrl,
                FormatHeaders(request),
                requestBody ?? "(empty)");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            actualStatus = (int)response.StatusCode;
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("Received {StatusCode} from {Url}", actualStatus, requestUrl);
            _logger.LogDebug("Response body from {Url}: {Body}", requestUrl, responseBody);

            JsonNode? responseJson = null;

            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                try
                {
                    responseJson = JsonNode.Parse(responseBody);
                }
                catch (JsonException jsonException)
                {
                    _logger.LogWarning(jsonException, "Response body from {Url} is not valid JSON", requestUrl);
                }
            }

            var tests = endpoint.Tests.Select(test => ExecuteTest(environment, test, actualStatus.Value, responseJson)).ToArray();

            return new EndpointRunResult
            {
                Name = endpoint.Name,
                Method = endpoint.Method.ToUpperInvariant(),
                RequestUrl = requestUrl,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                ActualStatus = actualStatus,
                ResponseBody = responseBody,
                Tests = tests
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Endpoint '{EndpointName}' in environment '{EnvironmentName}' failed before tests completed", endpoint.Name, environment.Name);

            var failedTests = endpoint.Tests.Select(test => new TestCaseRunResult
            {
                Name = test.Name,
                ExpectedStatus = test.ExpectedStatus,
                ActualStatus = actualStatus,
                StatusMatched = false,
                IsSuccess = false,
                ErrorMessage = exception.Message,
                Assertions = []
            }).ToArray();

            return new EndpointRunResult
            {
                Name = endpoint.Name,
                Method = endpoint.Method.ToUpperInvariant(),
                RequestUrl = requestUrl,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                ActualStatus = actualStatus,
                ErrorMessage = exception.Message,
                ResponseBody = responseBody,
                Tests = failedTests
            };
        }
    }

    private TestCaseRunResult ExecuteTest(
        EnvironmentDefinition environment,
        TestDefinition test,
        int actualStatus,
        JsonNode? responseJson)
    {
        IReadOnlyList<AssertionDefinition> resolvedAssertions;

        try
        {
            resolvedAssertions = ResolveAssertions(test.Assertions, environment);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to resolve assertion variables for test '{TestName}'", test.Name);

            return new TestCaseRunResult
            {
                Name = test.Name,
                ExpectedStatus = test.ExpectedStatus,
                ActualStatus = actualStatus,
                StatusMatched = test.ExpectedStatus == actualStatus,
                IsSuccess = false,
                ErrorMessage = exception.Message,
                Assertions = []
            };
        }

        var statusMatched = test.ExpectedStatus == actualStatus;
        var assertions = _assertionEvaluator.EvaluateAll(resolvedAssertions, responseJson);

        if (!statusMatched)
        {
            _logger.LogWarning(
                "Test '{TestName}' failed status validation. Expected {ExpectedStatus} but received {ActualStatus}",
                test.Name,
                test.ExpectedStatus,
                actualStatus);
        }

        foreach (var failedAssertion in assertions.Where(assertion => !assertion.IsSuccess))
        {
            _logger.LogWarning(
                "Assertion failed for field '{Field}' using rule '{Rule}': {Message}",
                failedAssertion.Field,
                failedAssertion.Rule,
                failedAssertion.Message);
        }

        var failedMessages = assertions
            .Where(assertion => !assertion.IsSuccess)
            .Select(assertion => assertion.Message)
            .ToArray();

        return new TestCaseRunResult
        {
            Name = test.Name,
            ExpectedStatus = test.ExpectedStatus,
            ActualStatus = actualStatus,
            StatusMatched = statusMatched,
            IsSuccess = statusMatched && assertions.All(assertion => assertion.IsSuccess),
            ErrorMessage = failedMessages.Length == 0 ? null : string.Join(" ", failedMessages),
            Assertions = assertions
        };
    }

    private IReadOnlyList<AssertionDefinition> ResolveAssertions(
        IReadOnlyList<AssertionDefinition> assertions,
        EnvironmentDefinition environment)
    {
        return assertions.Select(assertion => new AssertionDefinition
        {
            Field = _variableResolver.ResolveRequiredString(assertion.Field, environment, "assertion field"),
            EqualsValue = _variableResolver.ResolveValue(assertion.EqualsValue, environment),
            NotEquals = _variableResolver.ResolveValue(assertion.NotEquals, environment),
            Type = assertion.Type is null
                ? null
                : _variableResolver.ResolveRequiredString(assertion.Type, environment, "assertion type"),
            ContainsText = assertion.ContainsText is null
                ? null
                : _variableResolver.ResolveRequiredString(assertion.ContainsText, environment, "assertion containsText"),
            StartsWith = assertion.StartsWith is null
                ? null
                : _variableResolver.ResolveRequiredString(assertion.StartsWith, environment, "assertion startsWith"),
            EndsWith = assertion.EndsWith is null
                ? null
                : _variableResolver.ResolveRequiredString(assertion.EndsWith, environment, "assertion endsWith"),
            NotEmpty = _variableResolver.ResolveValue(assertion.NotEmpty, environment),
            MinCount = _variableResolver.ResolveValue(assertion.MinCount, environment),
            MaxCount = _variableResolver.ResolveValue(assertion.MaxCount, environment),
            Count = _variableResolver.ResolveValue(assertion.Count, environment),
            Contains = assertion.Contains.Count == 0
                ? new Dictionary<string, object?>()
                : assertion.Contains.ToDictionary(
                    pair => _variableResolver.ResolveRequiredString(pair.Key, environment, "assertion contains key"),
                    pair => _variableResolver.ResolveValue(pair.Value, environment),
                    StringComparer.OrdinalIgnoreCase)
        }).ToArray();
    }

    private HttpRequestMessage BuildRequest(
        EnvironmentDefinition environment,
        EndpointDefinition endpoint,
        out string requestUrl,
        out string? requestBody)
    {
        var resolvedBaseUrl = _variableResolver.ResolveRequiredString(environment.BaseUrl, environment, "baseUrl");
        if (!Uri.TryCreate(resolvedBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Environment '{environment.Name}' has an invalid baseUrl: {resolvedBaseUrl}");
        }

        var normalizedMethod = endpoint.Method.ToUpperInvariant();
        if (!SupportedMethods.Contains(normalizedMethod))
        {
            throw new InvalidOperationException(
                $"Endpoint '{endpoint.Name}' uses unsupported HTTP method '{endpoint.Method}'. Supported methods: GET, POST, PUT, PATCH, DELETE.");
        }

        var resolvedPath = ResolvePath(endpoint.Path, endpoint.PathParams, environment);
        var combinedUri = new Uri(baseUri, resolvedPath);

        if (endpoint.Query.Count > 0)
        {
            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in endpoint.Query)
            {
                query[pair.Key] = ConvertToString(_variableResolver.ResolveValue(pair.Value, environment));
            }

            requestUrl = QueryHelpers.AddQueryString(combinedUri.ToString(), query);
        }
        else
        {
            requestUrl = combinedUri.ToString();
        }

        var method = new HttpMethod(normalizedMethod);
        var request = new HttpRequestMessage(method, requestUrl);
        requestBody = null;

        if (endpoint.Body is not null)
        {
            var resolvedBody = _variableResolver.ResolveValue(endpoint.Body, environment);
            requestBody = JsonNodeConversion.ToJsonNode(resolvedBody)?.ToJsonString(SerializerOptions) ?? "null";
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        }

        foreach (var header in endpoint.Headers)
        {
            var resolvedHeaderValue = _variableResolver.ResolveRequiredString(header.Value, environment, $"header '{header.Key}'");

            if (!request.Headers.TryAddWithoutValidation(header.Key, resolvedHeaderValue))
            {
                request.Content ??= new StringContent(string.Empty);
                request.Content.Headers.TryAddWithoutValidation(header.Key, resolvedHeaderValue);
            }
        }

        return request;
    }

    private string ResolvePath(string path, IReadOnlyDictionary<string, object?> pathParams, EnvironmentDefinition environment)
    {
        var resolvedPath = _variableResolver.ResolveRequiredString(path, environment, "path");

        foreach (var pathParam in pathParams)
        {
            var token = $"{{{pathParam.Key}}}";
            var resolvedPathParam = _variableResolver.ResolveValue(pathParam.Value, environment);
            resolvedPath = resolvedPath.Replace(token, Uri.EscapeDataString(ConvertToString(resolvedPathParam)), StringComparison.Ordinal);
        }

        return resolvedPath;
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => JsonSerializer.Serialize(value, SerializerOptions)
        };
    }

    private static string FormatHeaders(HttpRequestMessage request)
    {
        var headerPairs = request.Headers.Select(header => $"{header.Key}={string.Join("|", header.Value)}");
        var contentHeaderPairs = request.Content?.Headers.Select(header => $"{header.Key}={string.Join("|", header.Value)}")
            ?? Enumerable.Empty<string>();

        return string.Join(", ", headerPairs.Concat(contentHeaderPairs));
    }
}
