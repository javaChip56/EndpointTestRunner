using ApiTestRunner.App.Options;
using ApiTestRunner.App.Services;

namespace ApiTestRunner.App.Tests;

public sealed class CliArgumentParserTests
{
    [Fact]
    public void Parse_ReadsCiModeEnvironmentFilesAndOutputOptions()
    {
        var result = CliArgumentParser.Parse(
        [
            "--ci",
            "--env", "Uat,Prod",
            "--env", "Canary",
            "--file", "samples/endpoints/accounts.yaml",
            "--file", "samples/endpoints/customers.yaml",
            "--format", "junit",
            "--output", "artifacts/results.xml"
        ]);

        Assert.True(result.Enabled);
        Assert.Equal(CliOutputFormat.JUnit, result.OutputFormat);
        Assert.Equal("artifacts/results.xml", result.OutputPath);
        Assert.Equal(["Uat", "Prod", "Canary"], result.EnvironmentNames);
        Assert.Equal(
            ["samples/endpoints/accounts.yaml", "samples/endpoints/customers.yaml"],
            result.TestFiles);
    }

    [Fact]
    public void Parse_ThrowsForMissingOptionValue()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliArgumentParser.Parse(["--format"]));
        Assert.Contains("missing a value", exception.Message);
    }
}
