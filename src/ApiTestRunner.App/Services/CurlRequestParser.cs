using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApiTestRunner.App.Models;
using ApiTestRunner.Core.Models;

namespace ApiTestRunner.App.Services;

internal static class CurlRequestParser
{
    private static readonly Regex LineContinuationRegex = new(@"([\\`^])\s*\r?\n\s*", RegexOptions.Compiled);

    public static CurlRequestSummary Parse(string command)
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
            Path = GetUnescapedAbsolutePath(uri),
            Query = ParseQuery(uri),
            Headers = headers,
            Body = TryParseJsonBody(combinedBody),
            RawBody = combinedBody
        };
    }

    public static string ResolveRelativePath(EnvironmentDefinition environment, CurlRequestSummary request)
    {
        return TryMatchEnvironment(environment, request.Url)?.RelativePath ?? request.Path;
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
            _ => node.ToJsonString()
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

    public static EnvironmentMatch? TryMatchEnvironment(EnvironmentDefinition environment, string requestUrl)
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

        var environmentPath = NormalizePath(GetUnescapedAbsolutePath(environmentUri));
        var requestPath = NormalizePath(GetUnescapedAbsolutePath(requestUri));

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

    public static string NormalizePath(string path)
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

    private static string GetUnescapedAbsolutePath(Uri uri)
    {
        return string.IsNullOrWhiteSpace(uri.AbsolutePath)
            ? "/"
            : Uri.UnescapeDataString(uri.AbsolutePath);
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

    public sealed record EnvironmentMatch(EnvironmentDefinition Environment, string RelativePath);
}
