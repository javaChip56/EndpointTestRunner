namespace ApiTestRunner.App.Options;

public sealed class WebServerOptions
{
    public const string SectionName = "WebServer";

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 5005;

    public bool AutoLaunchBrowser { get; init; } = true;
}
