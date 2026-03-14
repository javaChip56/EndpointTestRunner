using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApiTestRunner.App.Models;
using ApiTestRunner.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
    private readonly ISerializer _yamlSerializer;

    public CurlCommandAnalyzer(IConfiguredTestSuiteProvider suiteProvider)
    {
        _suiteProvider = suiteProvider;
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    public async Task<CurlAnalyzeResponse> AnalyzeAsync(CurlAnalyzeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            throw new InvalidOperationException("Provide a cURL command to analyze.");
        }

        var parsedRequest = ParseCurlCommand(request.Command);
        var loadedSuite = await _suiteProvider.LoadAsync(cancellationToken);

        var matchedEnvironments = loadedSuite.Suite.Environments
            .Where(environment => BaseUrlsMatch(environment.BaseUrl, parsedRequest.BaseUrl))
            .OrderBy(environment => environment.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matchedEndpointEnvironments = matchedEnvironments
            .Where(environment => environment.Endpoints.Any(endpoint =>
                MethodsMatch(endpoint.Method, parsedRequest.Method) &&
                PathsMatch(endpoint.Path, parsedRequest.Path)))
            .Select(environment => environment.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var suggestedEnvironmentName = matchedEnvironments.Length > 0
            ? matchedEnvironments[0].Name
            : SuggestEnvironmentName(parsedRequest.BaseUrl);

        return new CurlAnalyzeResponse
        {
            Request = parsedRequest,
            Environment = new CurlEnvironmentAnalysis
            {
                Exists = matchedEnvironments.Length > 0,
                SuggestedName = suggestedEnvironmentName,
                MatchedEnvironmentNames = matchedEnvironments.Select(environment => environment.Name).ToArray(),
                SuggestedFilePath = matchedEnvironments.Length > 0
                    ? null
                    : BuildEnvironmentFilePath(suggestedEnvironmentName),
                SuggestedYaml = matchedEnvironments.Length > 0
                    ? null
                    : GenerateEnvironmentYaml(suggestedEnvironmentName, parsedRequest.BaseUrl)
            },
            Endpoint = new CurlEndpointAnalysis
            {
                Exists = matchedEndpointEnvironments.Length > 0,
                SuggestedName = SuggestEndpointName(parsedRequest),
                MatchedEnvironmentNames = matchedEndpointEnvironments,
                SuggestedFilePath = matchedEndpointEnvironments.Length > 0
                    ? null
                    : BuildEndpointFilePath(parsedRequest),
                SuggestedYaml = matchedEndpointEnvironments.Length > 0
                    ? null
                    : GenerateEndpointYaml(parsedRequest, matchedEnvironments, suggestedEnvironmentName)
            }
        };
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
                    method ??= "POST";
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

    private static bool BaseUrlsMatch(string left, string right)
    {
        return string.Equals(
            NormalizeBaseUrl(left),
            NormalizeBaseUrl(right),
            StringComparison.OrdinalIgnoreCase);
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

    private string GenerateEnvironmentYaml(string environmentName, string baseUrl)
    {
        var document = new
        {
            environments = new[]
            {
                new
                {
                    name = environmentName,
                    baseUrl
                }
            }
        };

        return _yamlSerializer.Serialize(document).Trim();
    }

    private string GenerateEndpointYaml(
        CurlRequestSummary request,
        IReadOnlyList<EnvironmentDefinition> matchedEnvironments,
        string fallbackEnvironmentName)
    {
        var targetEnvironments = matchedEnvironments.Count > 0
            ? matchedEnvironments.Select(environment => environment.Name).ToArray()
            : new[] { fallbackEnvironmentName };

        var endpointDocument = new Dictionary<string, object?>
        {
            ["targetEnvironments"] = targetEnvironments,
            ["endpoints"] = new[]
            {
                BuildEndpointDocument(request)
            }
        };

        return _yamlSerializer.Serialize(endpointDocument).Trim();
    }

    private Dictionary<string, object?> BuildEndpointDocument(CurlRequestSummary request)
    {
        var endpoint = new Dictionary<string, object?>
        {
            ["name"] = SuggestEndpointName(request),
            ["method"] = request.Method,
            ["path"] = request.Path
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

        endpoint["tests"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["name"] = $"{SuggestEndpointName(request)} should return success",
                ["expectedStatus"] = 200
            }
        };

        return endpoint;
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

    private static string SuggestEndpointName(CurlRequestSummary request)
    {
        var method = request.Method.ToUpperInvariant();
        var segments = request.Path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => TemplateSegmentRegex.IsMatch(segment) ? "ByParameter" : ToTitleCaseToken(segment))
            .ToArray();

        var pathPart = segments.Length == 0 ? "Root" : string.Concat(segments);
        return $"{method} {pathPart}";
    }

    private static string BuildEnvironmentFilePath(string environmentName)
    {
        return $"samples/environments/{ToSlug(environmentName)}.yaml";
    }

    private static string BuildEndpointFilePath(CurlRequestSummary request)
    {
        var segments = request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var folder = segments.Length > 0 ? ToSlug(segments[0]) : "root";
        var fileName = $"{request.Method.ToLowerInvariant()}-{ToSlug(segments.LastOrDefault() ?? "root")}.yaml";
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
}
