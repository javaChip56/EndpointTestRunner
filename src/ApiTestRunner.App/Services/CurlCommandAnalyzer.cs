using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApiTestRunner.App.Models;
using ApiTestRunner.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ApiTestRunner.App.Services;

public sealed class CurlCommandAnalyzer : ICurlCommandAnalyzer
{
    private static readonly Regex LineContinuationRegex = new(@"([\\`^])\s*\r?\n\s*", RegexOptions.Compiled);
    private static readonly Regex TemplateSegmentRegex = new(@"\{[^{}]+\}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConfiguredTestSuiteProvider _suiteProvider;

    public CurlCommandAnalyzer(IConfiguredTestSuiteProvider suiteProvider)
    {
        _suiteProvider = suiteProvider;
    }

    public async Task<CurlAnalyzeResponse> AnalyzeAsync(CurlAnalyzeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            throw new InvalidOperationException("Provide a cURL command to analyze.");
        }

        var parsedRequest = CurlRequestParser.Parse(request.Command);
        var warnings = new List<string>();
        var loadedSuite = await TryLoadSuiteAsync(warnings, cancellationToken);
        if (warnings.Count == 0 &&
            loadedSuite.FilePaths.Count == 0 &&
            loadedSuite.Suite.Environments.Count == 0)
        {
            warnings.Add("Warning: No YAML files were loaded from the configured suite.");
        }

        var variableSuggestions = BuildVariableSuggestions(parsedRequest);

        var matchedEnvironmentInfos = loadedSuite.Suite.Environments
            .Select(environment => CurlRequestParser.TryMatchEnvironment(environment, parsedRequest.Url))
            .Where(match => match is not null)
            .Select(match => match!)
            .OrderByDescending(match => NormalizeBaseUrl(match.Environment.BaseUrl).Length)
            .ThenBy(match => match.Environment.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matchedEnvironments = matchedEnvironmentInfos
            .Select(match => match.Environment)
            .DistinctBy(environment => environment.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matchedEnvironmentPreviews = matchedEnvironments
            .Select(environment => new CurlYamlPreview
            {
                Title = environment.Name,
                Yaml = SerializeYaml(new Dictionary<string, object?>
                {
                    ["environments"] = new object?[]
                    {
                        BuildEnvironmentDocument(environment)
                    }
                })
            })
            .ToArray();

        var effectivePath = matchedEnvironmentInfos.FirstOrDefault()?.RelativePath ?? parsedRequest.Path;
        var requestSummary = new CurlRequestSummary
        {
            Method = parsedRequest.Method,
            Url = parsedRequest.Url,
            BaseUrl = parsedRequest.BaseUrl,
            Path = parsedRequest.Path,
            RelativePath = effectivePath,
            Query = parsedRequest.Query,
            Headers = parsedRequest.Headers,
            Body = parsedRequest.Body,
            RawBody = parsedRequest.RawBody
        };

        var matchedEndpointMatches = matchedEnvironmentInfos
            .SelectMany(match => match.Environment.Endpoints
                .Where(endpoint =>
                    MethodsMatch(endpoint.Method, parsedRequest.Method) &&
                    PathsMatch(endpoint.Path, match.RelativePath))
                .Select(endpoint => new EndpointMatch(match.Environment, endpoint)))
            .ToArray();

        var matchedEndpointEnvironments = matchedEndpointMatches
            .Select(match => match.Environment.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matchedEndpointPreviews = matchedEndpointMatches
            .Select(match => new CurlYamlPreview
            {
                Title = $"{match.Environment.Name} - {match.Endpoint.Name}",
                Yaml = SerializeYaml(new Dictionary<string, object?>
                {
                    ["targetEnvironments"] = new[] { match.Environment.Name },
                    ["endpoints"] = new object?[]
                    {
                        BuildEndpointDocument(match.Endpoint)
                    }
                })
            })
            .ToArray();

        var suggestedEnvironmentName = matchedEnvironments.Length > 0
            ? matchedEnvironments[0].Name
            : SuggestEnvironmentName(parsedRequest.BaseUrl);

        var selectedEnvironmentName = string.IsNullOrWhiteSpace(request.EnvironmentId)
            ? null
            : loadedSuite.Suite.Environments
                .FirstOrDefault(environment =>
                    string.Equals(
                        DashboardSuiteManifestFactory.CreateEnvironmentId(environment),
                        request.EnvironmentId,
                        StringComparison.OrdinalIgnoreCase))
                ?.Name;

        var targetEnvironmentNames = !string.IsNullOrWhiteSpace(selectedEnvironmentName)
            ? [selectedEnvironmentName]
            : matchedEnvironmentInfos.Length > 0
                ? matchedEnvironmentInfos
                    .Where(match => string.Equals(match.RelativePath, effectivePath, StringComparison.OrdinalIgnoreCase))
                    .Select(match => match.Environment.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [suggestedEnvironmentName];

        var endpointName = string.IsNullOrWhiteSpace(request.EndpointName)
            ? SuggestEndpointName(parsedRequest.Method, effectivePath)
            : request.EndpointName.Trim();
        var testDrafts = NormalizeTestDrafts(request, parsedRequest.Method, effectivePath);
        var generatedEndpointYaml = GenerateEndpointYaml(
            variableSuggestions.TransformedRequest,
            effectivePath,
            endpointName,
            targetEnvironmentNames,
            testDrafts);

        return new CurlAnalyzeResponse
        {
            Request = requestSummary,
            Environment = new CurlEnvironmentAnalysis
            {
                Exists = matchedEnvironments.Length > 0,
                SuggestedName = suggestedEnvironmentName,
                MatchedEnvironmentNames = matchedEnvironments.Select(environment => environment.Name).ToArray(),
                MatchedYamlPreviews = matchedEnvironmentPreviews,
                SuggestedFilePath = matchedEnvironments.Length > 0
                    ? null
                    : BuildEnvironmentFilePath(suggestedEnvironmentName),
                SuggestedYaml = matchedEnvironments.Length > 0
                    ? null
                    : GenerateEnvironmentYaml(suggestedEnvironmentName, parsedRequest.BaseUrl, variableSuggestions.Variables)
            },
            Endpoint = new CurlEndpointAnalysis
            {
                Exists = matchedEndpointEnvironments.Length > 0,
                SuggestedName = endpointName,
                MatchedEnvironmentNames = matchedEndpointEnvironments,
                MatchedYamlPreviews = matchedEndpointPreviews,
                GeneratedYaml = generatedEndpointYaml,
                SuggestedFilePath = matchedEndpointEnvironments.Length > 0
                    ? null
                    : BuildEndpointFilePath(parsedRequest.Method, effectivePath),
                SuggestedYaml = matchedEndpointEnvironments.Length > 0
                    ? null
                    : generatedEndpointYaml
            },
            Variables = new CurlVariableAnalysis
            {
                HasSuggestions = variableSuggestions.Variables.Count > 0,
                VariableNames = variableSuggestions.Variables.Keys.ToArray(),
                SuggestedYaml = variableSuggestions.Variables.Count == 0
                    ? null
                    : GenerateVariablesYaml(variableSuggestions.Variables),
                IncludedInEnvironmentYaml = matchedEnvironments.Length == 0 && variableSuggestions.Variables.Count > 0
            },
            Warnings = warnings
        };
    }

    private async Task<LoadedTestSuite> TryLoadSuiteAsync(
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _suiteProvider.LoadAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            warnings.Add($"Warning: {exception.Message}");
            return new LoadedTestSuite(new ApiTestSuiteDefinition(), []);
        }
    }

    private VariableSuggestionResult BuildVariableSuggestions(CurlRequestSummary request)
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var transformedQuery = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in request.Query)
        {
            var variableName = RegisterVariable(variables, SuggestVariableName(pair.Key), pair.Value);
            transformedQuery[pair.Key] = CreateVariableToken(variableName);
        }

        var transformedBody = ReplaceBodyScalarsWithVariables(
            request.Body,
            variables,
            pathSegments: [],
            parentObject: null,
            currentKey: null);

        var transformedRequest = new CurlRequestSummary
        {
            Method = request.Method,
            Url = request.Url,
            BaseUrl = request.BaseUrl,
            Path = request.Path,
            RelativePath = request.RelativePath,
            Query = transformedQuery,
            Headers = request.Headers,
            Body = transformedBody,
            RawBody = request.RawBody
        };

        return new VariableSuggestionResult(variables, transformedRequest);
    }

    private static CurlRequestSummary ParseCurlCommand(string command)
    {
        var normalizedCommand = LineContinuationRegex.Replace(command.Trim(), " ");
        var tokens = Tokenize(normalizedCommand);

        if (tokens.Count == 0)
        {
            throw new InvalidOperationException("The provided cURL command was empty.");
        }

        var currentIndex = 0;
        if (tokens[0].Equals("curl", StringComparison.OrdinalIgnoreCase) ||
            tokens[0].Equals("curl.exe", StringComparison.OrdinalIgnoreCase))
        {
            currentIndex++;
        }

        string? method = null;
        string? url = null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodySegments = new List<string>();

        while (currentIndex < tokens.Count)
        {
            var token = tokens[currentIndex];

            switch (token)
            {
                case "-X":
                case "--request":
                    method = ReadNextValue(tokens, ref currentIndex, token);
                    break;
                case "-H":
                case "--header":
                    var headerValue = ReadNextValue(tokens, ref currentIndex, token);
                    AddHeader(headers, headerValue);
                    break;
                case "-d":
                case "--data":
                case "--data-raw":
                case "--data-binary":
                    bodySegments.Add(ReadNextValue(tokens, ref currentIndex, token));
                    method ??= HttpMethod.Post.Method;
                    break;
                case "--url":
                    url = ReadNextValue(tokens, ref currentIndex, token);
                    break;
                default:
                    if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        token.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        url = token;
                    }

                    break;
            }

            currentIndex++;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("No URL was found in the cURL command.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"The cURL command contained an invalid URL: {url}");
        }

        var combinedBody = bodySegments.Count == 0 ? null : string.Join(Environment.NewLine, bodySegments);

        return new CurlRequestSummary
        {
            Method = (method ?? (combinedBody is null ? HttpMethod.Get.Method : HttpMethod.Post.Method)).ToUpperInvariant(),
            Url = uri.ToString(),
            BaseUrl = $"{uri.Scheme}://{uri.Authority}",
            Path = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : uri.AbsolutePath,
            Query = ParseQuery(uri),
            Headers = headers,
            Body = TryParseJsonBody(combinedBody),
            RawBody = combinedBody
        };
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var segments = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            var parts = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            query[key] = value;
        }

        return query;
    }

    private static object? TryParseJsonBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(body);
            return ConvertJsonNode(node);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static object? ConvertJsonNode(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonObject jsonObject => jsonObject.ToDictionary(
                pair => pair.Key,
                pair => ConvertJsonNode(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonArray jsonArray => jsonArray.Select(ConvertJsonNode).ToList(),
            JsonValue jsonValue => ConvertJsonScalar(jsonValue),
            _ => node.ToJsonString(JsonSerializerOptions)
        };
    }

    private static object? ConvertJsonScalar(JsonValue value)
    {
        if (value.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        if (value.TryGetValue<bool>(out var booleanValue))
        {
            return booleanValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        return value.ToJsonString();
    }

    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var index = 0; index < command.Length; index++)
        {
            var character = command[index];

            if (inSingleQuotes)
            {
                if (character == '\'')
                {
                    inSingleQuotes = false;
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (inDoubleQuotes)
            {
                if (character == '"' && !IsEscaped(command, index))
                {
                    inDoubleQuotes = false;
                }
                else if (character == '\\' && index + 1 < command.Length)
                {
                    current.Append(command[++index]);
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                FlushToken(tokens, current);
                continue;
            }

            if (character == '\'')
            {
                inSingleQuotes = true;
                continue;
            }

            if (character == '"')
            {
                inDoubleQuotes = true;
                continue;
            }

            if (character == '\\' && index + 1 < command.Length)
            {
                current.Append(command[++index]);
                continue;
            }

            current.Append(character);
        }

        if (inSingleQuotes || inDoubleQuotes)
        {
            throw new InvalidOperationException("The cURL command contains an unmatched quote.");
        }

        FlushToken(tokens, current);
        return tokens;
    }

    private static void FlushToken(ICollection<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static bool IsEscaped(string input, int index)
    {
        var slashCount = 0;

        for (var i = index - 1; i >= 0 && input[i] == '\\'; i--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    private static string ReadNextValue(IReadOnlyList<string> tokens, ref int currentIndex, string option)
    {
        if (currentIndex + 1 >= tokens.Count)
        {
            throw new InvalidOperationException($"The cURL option '{option}' is missing its value.");
        }

        currentIndex++;
        return tokens[currentIndex];
    }

    private static void AddHeader(IDictionary<string, string> headers, string headerValue)
    {
        var separatorIndex = headerValue.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return;
        }

        var headerName = headerValue[..separatorIndex].Trim();
        var headerContent = headerValue[(separatorIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(headerName))
        {
            return;
        }

        headers[headerName] = headerContent;
    }

    private static EnvironmentMatch? TryMatchEnvironment(EnvironmentDefinition environment, string requestUrl)
    {
        if (!Uri.TryCreate(environment.BaseUrl, UriKind.Absolute, out var environmentUri) ||
            !Uri.TryCreate(requestUrl, UriKind.Absolute, out var requestUri))
        {
            return null;
        }

        if (!string.Equals(environmentUri.Scheme, requestUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(environmentUri.Host, requestUri.Host, StringComparison.OrdinalIgnoreCase) ||
            environmentUri.Port != requestUri.Port)
        {
            return null;
        }

        var environmentPath = NormalizePath(environmentUri.AbsolutePath);
        var requestPath = NormalizePath(requestUri.AbsolutePath);

        if (!PathStartsWith(requestPath, environmentPath))
        {
            return null;
        }

        var relativePath = requestPath[environmentPath.Length..];
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = "/";
        }
        else if (!relativePath.StartsWith('/'))
        {
            relativePath = "/" + relativePath;
        }

        return new EnvironmentMatch(environment, NormalizePath(relativePath));
    }

    private static bool PathStartsWith(string requestPath, string environmentPath)
    {
        if (string.Equals(environmentPath, "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!requestPath.StartsWith(environmentPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return requestPath.Length == environmentPath.Length ||
               requestPath[environmentPath.Length] == '/';
    }

    private static string NormalizeBaseUrl(string value)
    {
        return value.Trim().TrimEnd('/');
    }

    private static bool MethodsMatch(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsMatch(string templatePath, string actualPath)
    {
        var normalizedTemplatePath = NormalizePath(templatePath);
        var normalizedActualPath = NormalizePath(actualPath);

        if (string.Equals(normalizedTemplatePath, normalizedActualPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var templateSegments = normalizedTemplatePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var actualSegments = normalizedActualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (templateSegments.Length != actualSegments.Length)
        {
            return false;
        }

        for (var index = 0; index < templateSegments.Length; index++)
        {
            if (TemplateSegmentRegex.IsMatch(templateSegments[index]))
            {
                continue;
            }

            if (!string.Equals(templateSegments[index], actualSegments[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var normalized = path.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    private object? ReplaceBodyScalarsWithVariables(
        object? value,
        IDictionary<string, object?> variables,
        IReadOnlyList<string> pathSegments,
        IReadOnlyDictionary<string, object?>? parentObject,
        string? currentKey)
    {
        switch (value)
        {
            case null:
                return null;
            case IDictionary<string, object?> dictionary:
            {
                var transformed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                foreach (var pair in dictionary)
                {
                    transformed[pair.Key] = ReplaceBodyScalarsWithVariables(
                        pair.Value,
                        variables,
                        pathSegments.Concat([pair.Key]).ToArray(),
                        (IReadOnlyDictionary<string, object?>)dictionary,
                        pair.Key);
                }

                return transformed;
            }
            case IEnumerable<object?> sequence when value is not string:
            {
                var items = sequence.ToArray();
                var transformedItems = new List<object?>(items.Length);

                for (var index = 0; index < items.Length; index++)
                {
                    transformedItems.Add(ReplaceBodyScalarsWithVariables(
                        items[index],
                        variables,
                        pathSegments.Concat([$"item{index + 1}"]).ToArray(),
                        parentObject: null,
                        currentKey: null));
                }

                return transformedItems;
            }
            case IEnumerable sequence when value is not string:
            {
                var items = sequence.Cast<object?>().ToArray();
                var transformedItems = new List<object?>(items.Length);

                for (var index = 0; index < items.Length; index++)
                {
                    transformedItems.Add(ReplaceBodyScalarsWithVariables(
                        items[index],
                        variables,
                        pathSegments.Concat([$"item{index + 1}"]).ToArray(),
                        parentObject: null,
                        currentKey: null));
                }

                return transformedItems;
            }
            case string or bool or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
            {
                if (!ShouldPromoteBodyScalar(currentKey, value))
                {
                    return value;
                }

                var variableName = RegisterVariable(
                    variables,
                    SuggestVariableName(pathSegments, parentObject, currentKey),
                    value);

                return CreateVariableToken(variableName);
            }
            default:
                return value;
        }
    }

    private static bool ShouldPromoteBodyScalar(string? currentKey, object? value)
    {
        if (value is null)
        {
            return false;
        }

        return !string.Equals(currentKey, "column", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentKey, "filterType", StringComparison.OrdinalIgnoreCase);
    }

    private string GenerateEnvironmentYaml(string environmentName, string baseUrl, IReadOnlyDictionary<string, object?> variables)
    {
        var document = new Dictionary<string, object?>
        {
            ["environments"] = new object?[]
            {
                BuildEnvironmentDocument(new EnvironmentDefinition
                {
                    Name = environmentName,
                    BaseUrl = baseUrl,
                    Variables = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase)
                })
            }
        };

        return SerializeYaml(document);
    }

    private string GenerateVariablesYaml(IReadOnlyDictionary<string, object?> variables)
    {
        return SerializeYaml(new Dictionary<string, object?>
        {
            ["variables"] = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase)
        });
    }

    private string GenerateEndpointYaml(
        CurlRequestSummary request,
        string endpointPath,
        string endpointName,
        IReadOnlyList<string> targetEnvironmentNames,
        IReadOnlyList<CurlTestDraft> tests)
    {
        var endpointDocument = new Dictionary<string, object?>
        {
            ["targetEnvironments"] = targetEnvironmentNames,
            ["endpoints"] = new[]
            {
                BuildEndpointDocument(request, endpointPath, endpointName, tests)
            }
        };

        return SerializeYaml(endpointDocument);
    }

    private Dictionary<string, object?> BuildEndpointDocument(
        CurlRequestSummary request,
        string endpointPath,
        string endpointName,
        IReadOnlyList<CurlTestDraft> tests)
    {
        var endpoint = new Dictionary<string, object?>
        {
            ["name"] = endpointName,
            ["method"] = request.Method,
            ["path"] = endpointPath
        };

        if (request.Headers.Count > 0)
        {
            endpoint["headers"] = request.Headers
                .Where(pair => !string.Equals(pair.Key, "Host", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(pair.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (request.Query.Count > 0)
        {
            endpoint["query"] = request.Query.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (request.Body is not null)
        {
            endpoint["body"] = request.Body;
        }

        endpoint["tests"] = BuildTestDocuments(request.Method, endpointPath, tests);
        return endpoint;
    }

    private static Dictionary<string, object?> BuildEnvironmentDocument(EnvironmentDefinition environment)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = environment.Name,
            ["baseUrl"] = environment.BaseUrl,
            ["variables"] = environment.Variables.Count == 0
                ? null
                : new Dictionary<string, object?>(environment.Variables, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static Dictionary<string, object?> BuildEndpointDocument(EndpointDefinition endpoint)
    {
        var document = new Dictionary<string, object?>
        {
            ["name"] = endpoint.Name,
            ["method"] = endpoint.Method,
            ["path"] = endpoint.Path
        };

        if (endpoint.PathParams.Count > 0)
        {
            document["pathParams"] = new Dictionary<string, object?>(endpoint.PathParams, StringComparer.OrdinalIgnoreCase);
        }

        if (endpoint.Query.Count > 0)
        {
            document["query"] = new Dictionary<string, object?>(endpoint.Query, StringComparer.OrdinalIgnoreCase);
        }

        if (endpoint.Headers.Count > 0)
        {
            document["headers"] = endpoint.Headers.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        if (endpoint.Body is not null)
        {
            document["body"] = endpoint.Body;
        }

        if (endpoint.Tests.Count > 0)
        {
            document["tests"] = endpoint.Tests.Select(BuildTestDocument).ToList();
        }

        return document;
    }

    private static List<Dictionary<string, object?>> BuildTestDocuments(
        string method,
        string endpointPath,
        IReadOnlyList<CurlTestDraft> tests)
    {
        var normalizedTests = tests
            .Where(test => !string.IsNullOrWhiteSpace(test.Name))
            .ToArray();

        if (normalizedTests.Length == 0)
        {
            normalizedTests =
            [
                new CurlTestDraft
                {
                    Name = $"{SuggestEndpointName(method, endpointPath)} should return success",
                    ExpectedStatus = 200,
                    Assertions = []
                }
            ];
        }

        var documents = new List<Dictionary<string, object?>>(normalizedTests.Length);

        foreach (var test in normalizedTests)
        {
            var testDefinition = new Dictionary<string, object?>
            {
                ["name"] = test.Name,
                ["expectedStatus"] = test.ExpectedStatus
            };

            var assertionDocuments = BuildAssertionDocuments(test.Assertions);
            if (assertionDocuments.Count > 0)
            {
                testDefinition["assertions"] = assertionDocuments;
            }

            documents.Add(testDefinition);
        }

        return documents;
    }

    private static List<Dictionary<string, object?>> BuildAssertionDocuments(IReadOnlyList<CurlAssertionDraft> assertions)
    {
        var documents = new List<Dictionary<string, object?>>();

        foreach (var assertion in assertions)
        {
            if (string.IsNullOrWhiteSpace(assertion.Field) || string.IsNullOrWhiteSpace(assertion.Rule))
            {
                continue;
            }

            var document = new Dictionary<string, object?>
            {
                ["field"] = assertion.Field
            };

            var rule = assertion.Rule.Trim();
            var normalizedRule = char.ToLowerInvariant(rule[0]) + rule[1..];
            document[normalizedRule] = ConvertAssertionValue(assertion.Value);
            documents.Add(document);
        }

        return documents;
    }

    private static Dictionary<string, object?> BuildTestDocument(TestDefinition test)
    {
        var document = new Dictionary<string, object?>
        {
            ["name"] = test.Name,
            ["expectedStatus"] = test.ExpectedStatus
        };

        if (test.Assertions.Count > 0)
        {
            document["assertions"] = test.Assertions
                .Select(BuildAssertionDocument)
                .ToList();
        }

        return document;
    }

    private static Dictionary<string, object?> BuildAssertionDocument(AssertionDefinition assertion)
    {
        var document = new Dictionary<string, object?>
        {
            ["field"] = assertion.Field
        };

        AddAssertionValue(document, "equals", assertion.EqualsValue);
        AddAssertionValue(document, "notEquals", assertion.NotEquals);
        AddAssertionValue(document, "type", assertion.Type);
        AddAssertionValue(document, "containsText", assertion.ContainsText);
        AddAssertionValue(document, "startsWith", assertion.StartsWith);
        AddAssertionValue(document, "endsWith", assertion.EndsWith);
        AddAssertionValue(document, "notEmpty", assertion.NotEmpty);
        AddAssertionValue(document, "minCount", assertion.MinCount);
        AddAssertionValue(document, "maxCount", assertion.MaxCount);
        AddAssertionValue(document, "count", assertion.Count);
        AddAssertionValue(document, "greaterThan", assertion.GreaterThan);
        AddAssertionValue(document, "greaterThanOrEqual", assertion.GreaterThanOrEqual);
        AddAssertionValue(document, "lessThan", assertion.LessThan);
        AddAssertionValue(document, "lessThanOrEqual", assertion.LessThanOrEqual);

        if (assertion.Contains.Count > 0)
        {
            document["contains"] = new Dictionary<string, object?>(assertion.Contains, StringComparer.OrdinalIgnoreCase);
        }

        return document;
    }

    private static void AddAssertionValue(
        IDictionary<string, object?> document,
        string key,
        object? value)
    {
        if (value is null)
        {
            return;
        }

        document[key] = value;
    }

    private static IReadOnlyList<CurlTestDraft> NormalizeTestDrafts(
        CurlAnalyzeRequest request,
        string method,
        string endpointPath)
    {
        if (request.Tests.Count > 0)
        {
            return request.Tests
                .Where(test => !string.IsNullOrWhiteSpace(test.Name))
                .Select(test => new CurlTestDraft
                {
                    Name = test.Name,
                    ExpectedStatus = test.ExpectedStatus <= 0 ? 200 : test.ExpectedStatus,
                    Assertions = test.Assertions
                        .Where(assertion => !string.IsNullOrWhiteSpace(assertion.Field) && !string.IsNullOrWhiteSpace(assertion.Rule))
                        .ToArray()
                })
                .ToArray();
        }

        return
        [
            new CurlTestDraft
            {
                Name = $"{SuggestEndpointName(method, endpointPath)} should return success",
                ExpectedStatus = 200,
                Assertions = request.Assertions
                    .Where(assertion => !string.IsNullOrWhiteSpace(assertion.Field) && !string.IsNullOrWhiteSpace(assertion.Rule))
                    .ToArray()
            }
        ];
    }

    private static object? ConvertAssertionValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement element => ConvertJsonElement(element),
            _ => value
        };
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText(), JsonSerializerOptions),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(element.GetRawText(), JsonSerializerOptions),
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static string SuggestEnvironmentName(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return "GeneratedEnvironment";
        }

        var hostSegments = uri.Host
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(ToTitleCaseToken)
            .ToArray();

        var portSuffix = uri.IsDefaultPort ? string.Empty : uri.Port.ToString();
        var suggested = string.Concat(hostSegments) + portSuffix;
        return string.IsNullOrWhiteSpace(suggested) ? "GeneratedEnvironment" : suggested;
    }

    private static string SuggestEndpointName(string method, string path)
    {
        var segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => TemplateSegmentRegex.IsMatch(segment) ? "ByParameter" : ToTitleCaseToken(segment))
            .ToArray();

        var pathPart = segments.Length == 0 ? "Root" : string.Concat(segments);
        return $"{method.ToUpperInvariant()} {pathPart}";
    }

    private static string BuildEnvironmentFilePath(string environmentName)
    {
        return $"samples/environments/{ToSlug(environmentName)}.yaml";
    }

    private static string BuildEndpointFilePath(string method, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var folder = segments.Length > 0 ? ToSlug(segments[0]) : "root";
        var fileName = $"{method.ToLowerInvariant()}-{ToSlug(segments.LastOrDefault() ?? "root")}.yaml";
        return $"samples/endpoints/{folder}/{fileName}";
    }

    private static string ToTitleCaseToken(string value)
    {
        var sanitized = Regex.Replace(value, @"[^A-Za-z0-9]+", " ");
        var words = sanitized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());

        return string.Concat(words);
    }

    private static string ToSlug(string value)
    {
        var normalized = Regex.Replace(value, @"[^A-Za-z0-9]+", "-")
            .Trim('-')
            .ToLowerInvariant();

        return string.IsNullOrWhiteSpace(normalized) ? "generated" : normalized;
    }

    private static string RegisterVariable(
        IDictionary<string, object?> variables,
        string baseName,
        object? value)
    {
        var normalizedBaseName = string.IsNullOrWhiteSpace(baseName) ? "value" : baseName;
        var candidate = normalizedBaseName;
        var suffix = 2;

        while (variables.TryGetValue(candidate, out var existingValue))
        {
            if (Equals(existingValue, value))
            {
                return candidate;
            }

            candidate = $"{normalizedBaseName}{suffix}";
            suffix++;
        }

        variables[candidate] = value;
        return candidate;
    }

    private static string CreateVariableToken(string variableName)
    {
        return $"{{{{var:{variableName}}}}}";
    }

    private static string SuggestVariableName(string key)
    {
        return ToCamelCase(key);
    }

    private static string SuggestVariableName(
        IReadOnlyList<string> pathSegments,
        IReadOnlyDictionary<string, object?>? parentObject,
        string? currentKey)
    {
        if (string.Equals(currentKey, "value", StringComparison.OrdinalIgnoreCase) &&
            parentObject is not null &&
            parentObject.TryGetValue("column", out var columnValue) &&
            columnValue is string columnName &&
            !string.IsNullOrWhiteSpace(columnName))
        {
            return ToCamelCase(columnName);
        }

        var filteredSegments = pathSegments
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Where(segment => !segment.StartsWith("item", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (filteredSegments.Length == 0)
        {
            return "value";
        }

        return ToCamelCase(string.Join(" ", filteredSegments));
    }

    private static string ToCamelCase(string value)
    {
        var parts = Regex.Matches(value, "[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+")
            .Select(match => match.Value)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length == 0)
        {
            return "value";
        }

        var builder = new StringBuilder();
        builder.Append(parts[0][..1].ToLowerInvariant());
        builder.Append(parts[0].Length > 1 ? parts[0][1..] : string.Empty);

        for (var index = 1; index < parts.Length; index++)
        {
            builder.Append(parts[index][..1].ToUpperInvariant());
            builder.Append(parts[index].Length > 1 ? parts[index][1..] : string.Empty);
        }

        return builder.ToString();
    }

    private static bool IsMissingYamlConfigurationException(Exception exception)
    {
        return exception is FileNotFoundException or DirectoryNotFoundException ||
               exception is InvalidOperationException invalidOperationException &&
               (invalidOperationException.Message.Contains("did not match any files", StringComparison.OrdinalIgnoreCase) ||
                invalidOperationException.Message.Contains("No YAML test files were configured", StringComparison.OrdinalIgnoreCase));
    }

    private static string SerializeYaml(object value)
    {
        var stream = new YamlStream(new YamlDocument(BuildYamlNode(value, isKey: false)));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString().Trim();
    }

    private static YamlNode BuildYamlNode(object? value, bool isKey)
    {
        return value switch
        {
            null => new YamlScalarNode("null"),
            string text => new YamlScalarNode(text)
            {
                Style = isKey ? ScalarStyle.Plain : ScalarStyle.DoubleQuoted
            },
            bool boolean => new YamlScalarNode(boolean ? "true" : "false"),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => new YamlScalarNode(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)),
            IDictionary<string, object?> dictionary => BuildMappingNode(dictionary),
            IEnumerable<object?> sequence => BuildSequenceNode(sequence),
            IEnumerable sequence when value is not string => BuildSequenceNode(sequence.Cast<object?>()),
            _ => new YamlScalarNode(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
            {
                Style = ScalarStyle.DoubleQuoted
            }
        };
    }

    private static YamlMappingNode BuildMappingNode(IEnumerable<KeyValuePair<string, object?>> values)
    {
        var mappingNode = new YamlMappingNode();

        foreach (var pair in values)
        {
            if (pair.Value is null)
            {
                continue;
            }

            mappingNode.Add(BuildYamlNode(pair.Key, isKey: true), BuildYamlNode(pair.Value, isKey: false));
        }

        return mappingNode;
    }

    private static YamlSequenceNode BuildSequenceNode(IEnumerable<object?> values)
    {
        var sequenceNode = new YamlSequenceNode();

        foreach (var item in values)
        {
            sequenceNode.Add(BuildYamlNode(item, isKey: false));
        }

        return sequenceNode;
    }

    private sealed record EnvironmentMatch(EnvironmentDefinition Environment, string RelativePath);

    private sealed record EndpointMatch(EnvironmentDefinition Environment, EndpointDefinition Endpoint);

    private sealed record VariableSuggestionResult(
        IReadOnlyDictionary<string, object?> Variables,
        CurlRequestSummary TransformedRequest);
}
