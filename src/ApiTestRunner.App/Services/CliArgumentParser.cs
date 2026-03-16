using ApiTestRunner.App.Options;

namespace ApiTestRunner.App.Services;

public static class CliArgumentParser
{
    public static CliExecutionOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var environmentNames = new List<string>();
        var testFiles = new List<string>();
        var enabled = false;
        var outputFormat = CliOutputFormat.None;
        string? outputPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--ci":
                    enabled = true;
                    break;
                case "--env":
                    environmentNames.AddRange(ReadDelimitedValues(args, ref index, argument));
                    break;
                case "--file":
                    testFiles.Add(ReadSingleValue(args, ref index, argument));
                    break;
                case "--format":
                    outputFormat = ParseOutputFormat(ReadSingleValue(args, ref index, argument));
                    break;
                case "--output":
                    outputPath = ReadSingleValue(args, ref index, argument);
                    break;
            }
        }

        return new CliExecutionOptions
        {
            Enabled = enabled,
            EnvironmentNames = environmentNames
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            TestFiles = testFiles
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            OutputFormat = outputFormat,
            OutputPath = string.IsNullOrWhiteSpace(outputPath) ? null : outputPath
        };
    }

    private static IReadOnlyList<string> ReadDelimitedValues(string[] args, ref int index, string option)
    {
        var value = ReadSingleValue(args, ref index, option);
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ReadSingleValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"The CLI option '{option}' is missing a value.");
        }

        index++;
        return args[index];
    }

    private static CliOutputFormat ParseOutputFormat(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "none" => CliOutputFormat.None,
            "json" => CliOutputFormat.Json,
            "junit" => CliOutputFormat.JUnit,
            "junitxml" => CliOutputFormat.JUnit,
            _ => throw new InvalidOperationException(
                $"Unsupported CLI output format '{value}'. Supported values: none, json, junit.")
        };
    }
}
