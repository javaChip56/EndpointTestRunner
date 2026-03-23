using System.Text.Json.Nodes;
using ApiTestRunner.App.Models;
using ApiTestRunner.App.Options;
using ApiTestRunner.App.Services;
using ApiTestRunner.Core.Extensions;
using ApiTestRunner.Core.Services;
using Microsoft.Extensions.Options;

var cliExecutionOptions = CliArgumentParser.Parse(args);
var builder = WebApplication.CreateBuilder(args);

var webServerOptions = builder.Configuration
    .GetSection(WebServerOptions.SectionName)
    .Get<WebServerOptions>() ?? new WebServerOptions();

builder.WebHost.UseUrls($"http://{webServerOptions.Host}:{webServerOptions.Port}");

builder.Services.Configure<WebServerOptions>(builder.Configuration.GetSection(WebServerOptions.SectionName));
builder.Services.Configure<ExecutionOptions>(builder.Configuration.GetSection(ExecutionOptions.SectionName));
builder.Services.AddSingleton(Options.Create(cliExecutionOptions));

builder.Services.AddApiTestRunnerCore();
builder.Services.AddSingleton<IConfiguredTestSuiteProvider, ConfiguredTestSuiteProvider>();
builder.Services.AddSingleton<ICurlCommandAnalyzer, CurlCommandAnalyzer>();
builder.Services.AddSingleton<DashboardEndpointEditorService>();
builder.Services.AddSingleton<TestRunCoordinator>();
builder.Services.AddSingleton<CliResultWriter>();
if (cliExecutionOptions.Enabled)
{
    builder.Services.AddHostedService<CliExecutionHostedService>();
}
else
{
    builder.Services.AddHostedService<StartupAutomationHostedService>();
}

builder.Services.AddHttpClient<IApiTestExecutor, ApiTestExecutor>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ExecutionOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/dashboard/state", (TestRunCoordinator coordinator) =>
{
    return Results.Ok(coordinator.GetState());
});

app.MapGet("/api/dashboard/manifest", async (TestRunCoordinator coordinator, CancellationToken cancellationToken) =>
{
    try
    {
        var manifest = await coordinator.GetManifestAsync(cancellationToken);
        return Results.Ok(manifest);
    }
    catch (Exception exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/dashboard/editor-seed", async (
    string environmentId,
    string endpointId,
    TestRunCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    try
    {
        var seed = await coordinator.GetEditorSeedAsync(environmentId, endpointId, cancellationToken);
        return Results.Ok(seed);
    }
    catch (Exception exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/dashboard/editor-save", async (
    DashboardEndpointSaveRequest request,
    TestRunCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await coordinator.SaveEditorAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (Exception exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/dashboard/run", async (HttpRequest request, TestRunCoordinator coordinator, CancellationToken cancellationToken) =>
{
    var selection = request.ContentLength > 0
        ? await request.ReadFromJsonAsync<TestSelectionRequest>(cancellationToken)
        : null;

    var result = await coordinator.ExecuteAsync(selection, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/tools/curl/analyze", async (CurlAnalyzeRequest request, ICurlCommandAnalyzer analyzer, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await analyzer.AnalyzeAsync(request, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/sample-api/health", () =>
{
    return Results.Json(new
    {
        success = true,
        message = "healthy",
        data = new
        {
            service = "ApiTestRunner Sample API",
            version = "1.0.0"
        }
    });
});

app.MapPost("/sample-api/auth/login", async (HttpContext httpContext) =>
{
    var requestJson = await JsonNode.ParseAsync(httpContext.Request.Body);
    var username = requestJson?["username"]?.GetValue<string>();
    var password = requestJson?["password"]?.GetValue<string>();

    if (username == "testuser" && password == "password123")
    {
        return Results.Json(new
        {
            success = true,
            message = "login success",
            data = new
            {
                token = "sample-token-123",
                user = new
                {
                    username = "testuser",
                    role = "tester"
                }
            }
        });
    }

    return Results.Json(new
    {
        success = false,
        message = "invalid credentials"
    }, statusCode: StatusCodes.Status401Unauthorized);
});

app.MapGet("/sample-api/accounts", (string? customerId, int? page, int? pageSize) =>
{
    return Results.Json(new
    {
        success = true,
        message = "success",
        data = new
        {
            request = new
            {
                customerId,
                page = page ?? 1,
                pageSize = pageSize ?? 50
            },
            accounts = new[]
            {
                new
                {
                    accountNo = "ACC-1001",
                    status = "Active",
                    currency = "SGD"
                },
                new
                {
                    accountNo = "ACC-1002",
                    status = "Inactive",
                    currency = "USD"
                }
            }
        }
    });
});

app.MapGet("/sample-api/customers/{customerId}", (string customerId) =>
{
    return Results.Json(new
    {
        success = true,
        data = new
        {
            customer = new
            {
                customerId,
                name = "Ada Lovelace",
                tier = "Gold",
                tags = new[] { "priority", "digital" },
                profile = new
                {
                    locale = "en-SG",
                    notifications = true
                }
            }
        }
    });
});

app.MapFallbackToFile("index.html");

app.Run();
