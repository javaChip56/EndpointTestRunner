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
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly IAssertionEvaluator _assertionEvaluator;
    private readonly ILogger<ApiTestExecutor> _logger;

    public ApiTestExecutor(
        HttpClient httpClient,
        IAssertionEvaluator assertionEvaluator,
        ILogger<ApiTestExecutor> logger)
    {
        _httpClient = httpClient;
        _assertionEvaluator = assertionEvaluator;
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
        string? responseBody = null;
        int? actualStatus = null;

        try
        {
            using var request = BuildRequest(environment, endpoint, out requestUrl);

            _logger.LogInformation("Executing {Method} {Url}", request.Method.Method, requestUrl);

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

            var tests = endpoint.Tests.Select(test => ExecuteTest(test, actualStatus.Value, responseJson)).ToArray();

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

    private TestCaseRunResult ExecuteTest(TestDefinition test, int actualStatus, JsonNode? responseJson)
    {
        var statusMatched = test.ExpectedStatus == actualStatus;
        var assertions = _assertionEvaluator.EvaluateAll(test.Assertions, responseJson);

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

    private static HttpRequestMessage BuildRequest(
        EnvironmentDefinition environment,
        EndpointDefinition endpoint,
        out string requestUrl)
    {
        if (!Uri.TryCreate(environment.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Environment '{environment.Name}' has an invalid baseUrl: {environment.BaseUrl}");
        }

        var resolvedPath = ResolvePath(endpoint.Path, endpoint.PathParams);
        var combinedUri = new Uri(baseUri, resolvedPath);

        if (endpoint.Query.Count > 0)
        {
            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in endpoint.Query)
            {
                query[pair.Key] = ConvertToString(pair.Value);
            }

            requestUrl = QueryHelpers.AddQueryString(combinedUri.ToString(), query);
        }
        else
        {
            requestUrl = combinedUri.ToString();
        }

        var method = new HttpMethod(endpoint.Method.ToUpperInvariant());
        var request = new HttpRequestMessage(method, requestUrl);

        if (endpoint.Body is not null)
        {
            var jsonBody = JsonNodeConversion.ToJsonNode(endpoint.Body)?.ToJsonString(SerializerOptions) ?? "null";
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        foreach (var header in endpoint.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content ??= new StringContent(string.Empty);
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return request;
    }

    private static string ResolvePath(string path, IReadOnlyDictionary<string, object?> pathParams)
    {
        var resolvedPath = path;

        foreach (var pathParam in pathParams)
        {
            var token = $"{{{pathParam.Key}}}";
            resolvedPath = resolvedPath.Replace(token, Uri.EscapeDataString(ConvertToString(pathParam.Value)), StringComparison.Ordinal);
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
}
