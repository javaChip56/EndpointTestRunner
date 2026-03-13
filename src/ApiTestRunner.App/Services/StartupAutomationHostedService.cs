using System.Diagnostics;
using ApiTestRunner.App.Options;
using Microsoft.Extensions.Options;

namespace ApiTestRunner.App.Services;

public sealed class StartupAutomationHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IOptions<WebServerOptions> _webServerOptions;
    private readonly TestRunCoordinator _testRunCoordinator;
    private readonly ILogger<StartupAutomationHostedService> _logger;

    public StartupAutomationHostedService(
        IHostApplicationLifetime applicationLifetime,
        IOptions<WebServerOptions> webServerOptions,
        TestRunCoordinator testRunCoordinator,
        ILogger<StartupAutomationHostedService> logger)
    {
        _applicationLifetime = applicationLifetime;
        _webServerOptions = webServerOptions;
        _testRunCoordinator = testRunCoordinator;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _applicationLifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                var dashboardUrl = BuildDashboardUrl(_webServerOptions.Value);

                if (_webServerOptions.Value.AutoLaunchBrowser)
                {
                    TryLaunchBrowser(dashboardUrl);
                }

                await _testRunCoordinator.ExecuteAsync(CancellationToken.None);
            });
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void TryLaunchBrowser(string dashboardUrl)
    {
        try
        {
            _logger.LogInformation("Launching browser for dashboard at {DashboardUrl}", dashboardUrl);

            Process.Start(new ProcessStartInfo
            {
                FileName = dashboardUrl,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to auto-launch browser");
        }
    }

    private static string BuildDashboardUrl(WebServerOptions options)
    {
        var host = options.Host switch
        {
            "0.0.0.0" => "localhost",
            "*" => "localhost",
            "+" => "localhost",
            _ => options.Host
        };

        return $"http://{host}:{options.Port}";
    }
}
