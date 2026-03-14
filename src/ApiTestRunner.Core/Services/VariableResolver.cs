using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using ApiTestRunner.Core.Models;
using Microsoft.Extensions.Configuration;

namespace ApiTestRunner.Core.Services;

public sealed class VariableResolver : IVariableResolver
{
    private static readonly Regex ExactTokenRegex = new(@"^\{\{\s*([^{}]+?)\s*\}\}$", RegexOptions.Compiled);
    private static readonly Regex EmbeddedTokenRegex = new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);

    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public VariableResolver(IConfiguration configuration, TimeProvider timeProvider)
    {
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public object? ResolveValue(object? value, EnvironmentDefinition environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return ResolveValueInternal(value, environment, []);
    }

    public string ResolveRequiredString(string? value, EnvironmentDefinition environment, string fieldName)
    {
        var resolved = ResolveValue(value, environment);
        return resolved switch
        {
            null => throw new InvalidOperationException($"Resolved value for '{fieldName}' in environment '{environment.Name}' was null."),
            string text => text,
            _ => ConvertToString(resolved)
        };
    }

    private object? ResolveValueInternal(object? value, EnvironmentDefinition environment, HashSet<string> variableStack)
    {
        return value switch
        {
            null => null,
            string text => ResolveString(text, environment, variableStack),
            IDictionary dictionary => ResolveDictionary(dictionary, environment, variableStack),
            IEnumerable sequence when value is not string => ResolveSequence(sequence, environment, variableStack),
            _ => value
        };
    }

    private object? ResolveString(string value, EnvironmentDefinition environment, HashSet<string> variableStack)
    {
        var exactMatch = ExactTokenRegex.Match(value);
        if (exactMatch.Success)
        {
            return EvaluateToken(exactMatch.Groups[1].Value, environment, variableStack);
        }

        return EmbeddedTokenRegex.Replace(value, match =>
        {
            var resolved = EvaluateToken(match.Groups[1].Value, environment, variableStack);
            return ConvertToString(resolved);
        });
    }

    private Dictionary<string, object?> ResolveDictionary(
        IDictionary dictionary,
        EnvironmentDefinition environment,
        HashSet<string> variableStack)
    {
        var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString() ?? string.Empty;
            resolved[key] = ResolveValueInternal(entry.Value, environment, variableStack);
        }

        return resolved;
    }

    private List<object?> ResolveSequence(
        IEnumerable sequence,
        EnvironmentDefinition environment,
        HashSet<string> variableStack)
    {
        var resolved = new List<object?>();

        foreach (var item in sequence)
        {
            resolved.Add(ResolveValueInternal(item, environment, variableStack));
        }

        return resolved;
    }

    private object? EvaluateToken(string tokenExpression, EnvironmentDefinition environment, HashSet<string> variableStack)
    {
        var token = tokenExpression.Trim();
        var separatorIndex = token.IndexOf(':');

        if (separatorIndex <= 0)
        {
            throw new InvalidOperationException($"Token '{token}' is invalid. Expected format '{{{{provider:value}}}}'.");
        }

        var provider = token[..separatorIndex].Trim().ToLowerInvariant();
        var argument = token[(separatorIndex + 1)..].Trim();

        return provider switch
        {
            "var" => ResolveEnvironmentVariable(argument, environment, variableStack),
            "env" => ResolveEnvironmentVariableValue(argument),
            "config" => ResolveConfigurationValue(argument),
            "now" => ResolveDateToken(_timeProvider.GetLocalNow(), argument),
            "today" => ResolveTodayToken(argument),
            _ => throw new InvalidOperationException($"Token provider '{provider}' is not supported.")
        };
    }

    private object? ResolveEnvironmentVariable(string variableName, EnvironmentDefinition environment, HashSet<string> variableStack)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            throw new InvalidOperationException("Environment variable token is missing a variable name.");
        }

        if (!environment.Variables.TryGetValue(variableName, out var value))
        {
            throw new InvalidOperationException(
                $"Environment variable '{variableName}' was not found in environment '{environment.Name}'.");
        }

        if (!variableStack.Add(variableName))
        {
            throw new InvalidOperationException(
                $"Circular environment variable reference detected for '{variableName}' in environment '{environment.Name}'.");
        }

        try
        {
            return ResolveValueInternal(value, environment, variableStack);
        }
        finally
        {
            variableStack.Remove(variableName);
        }
    }

    private static string ResolveEnvironmentVariableValue(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            throw new InvalidOperationException("Environment variable token is missing a variable name.");
        }

        return Environment.GetEnvironmentVariable(variableName)
            ?? throw new InvalidOperationException($"OS environment variable '{variableName}' was not found.");
    }

    private string ResolveConfigurationValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Configuration token is missing a key.");
        }

        var directValue = _configuration[key];
        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue;
        }

        var normalizedKey = key.Replace('.', ':');
        var normalizedValue = _configuration[normalizedKey];

        return normalizedValue
            ?? throw new InvalidOperationException($"Configuration key '{key}' was not found.");
    }

    private string ResolveTodayToken(string argument)
    {
        var localNow = _timeProvider.GetLocalNow();
        var today = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset);
        return ResolveDateToken(today, argument);
    }

    private static string ResolveDateToken(DateTimeOffset reference, string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            throw new InvalidOperationException("Date token must include a format, for example '{{now:yyyy-MM}}'.");
        }

        var segments = argument.Split(':', 2, StringSplitOptions.TrimEntries);
        var offsetSegment = segments.Length == 2 ? segments[0] : null;
        var format = segments.Length == 2 ? segments[1] : segments[0];

        if (!string.IsNullOrWhiteSpace(offsetSegment))
        {
            reference = ApplyOffset(reference, offsetSegment);
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            throw new InvalidOperationException("Date token format was empty.");
        }

        return reference.ToString(format, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ApplyOffset(DateTimeOffset value, string offsetSegment)
    {
        if (string.IsNullOrWhiteSpace(offsetSegment))
        {
            return value;
        }

        var sign = offsetSegment[0];
        if (sign is not ('+' or '-'))
        {
            throw new InvalidOperationException(
                $"Date offset '{offsetSegment}' is invalid. Expected formats like '+1d' or '-30d'.");
        }

        var unit = offsetSegment[^1];
        var amountText = offsetSegment[1..^1];

        if (!int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
        {
            throw new InvalidOperationException(
                $"Date offset '{offsetSegment}' is invalid. The numeric portion could not be parsed.");
        }

        if (sign == '-')
        {
            amount *= -1;
        }

        return unit switch
        {
            'd' or 'D' => value.AddDays(amount),
            'm' => value.AddMinutes(amount),
            'M' => value.AddMonths(amount),
            'h' or 'H' => value.AddHours(amount),
            'y' or 'Y' => value.AddYears(amount),
            _ => throw new InvalidOperationException(
                $"Date offset unit '{unit}' is not supported. Use d, M, y, h, or m.")
        };
    }

    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }
}
