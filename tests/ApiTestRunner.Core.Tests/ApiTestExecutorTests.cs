using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ApiTestRunner.Core.Models;
using ApiTestRunner.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiTestRunner.Core.Tests;

public sealed class ApiTestExecutorTests
{
    [Fact]
    public async Task RunAsync_ResolvesDynamicVariablesInOutgoingRequest()
    {
        const string envVarName = "API_TEST_RUNNER_TEST_TOKEN";
        var originalEnvVar = Environment.GetEnvironmentVariable(envVarName);

        try
        {
            Environment.SetEnvironmentVariable(envVarName, "secret-token");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Variables:DefaultCustomerId"] = "C1001"
                })
                .Build();

            var resolver = new VariableResolver(
                configuration,
                new FixedTimeProvider(new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.FromHours(8))));

            CapturingHandler? handler = null;
            handler = new CapturingHandler(async request =>
            {
                var body = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync();

                handler!.CapturedRequest = request;
                handler.CapturedBody = body;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "success": true }""", Encoding.UTF8, "application/json")
                };
            });

            var httpClient = new HttpClient(handler);
            var executor = new ApiTestExecutor(
                httpClient,
                new AssertionEvaluator(),
                resolver,
                NullLogger<ApiTestExecutor>.Instance);

            var suite = new ApiTestSuiteDefinition
            {
                Environments =
                [
                    new EnvironmentDefinition
                    {
                        Name = "Local",
                        BaseUrl = "https://localhost:7001",
                        Variables = new Dictionary<string, object?>
                        {
                            ["reportMonth"] = "{{now:MM}}",
                            ["reportYear"] = "{{now:yyyy}}"
                        },
                        Endpoints =
                        [
                            new EndpointDefinition
                            {
                                Name = "Get Report",
                                Method = "POST",
                                Path = "/api/reports/{customerId}",
                                PathParams = new Dictionary<string, object?>
                                {
                                    ["customerId"] = "{{config:Variables.DefaultCustomerId}}"
                                },
                                Query = new Dictionary<string, object?>
                                {
                                    ["month"] = "{{var:reportMonth}}",
                                    ["year"] = "{{var:reportYear}}",
                                    ["previousDay"] = "{{today:-1d:yyyy-MM-dd}}"
                                },
                                Headers = new Dictionary<string, string>
                                {
                                    ["Authorization"] = "Bearer {{env:API_TEST_RUNNER_TEST_TOKEN}}"
                                },
                                Body = new Dictionary<string, object?>
                                {
                                    ["period"] = "{{var:reportYear}}-{{var:reportMonth}}",
                                    ["customerId"] = "{{config:Variables.DefaultCustomerId}}",
                                    ["generatedOn"] = "{{today:yyyy-MM-dd}}"
                                },
                                Tests =
                                [
                                    new TestDefinition
                                    {
                                        Name = "Request should succeed",
                                        ExpectedStatus = 200,
                                        Assertions =
                                        [
                                            new AssertionDefinition
                                            {
                                                Field = "success",
                                                EqualsValue = true
                                            }
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };

            var result = await executor.RunAsync(suite, maxConcurrency: 1);
            var failureSummary = string.Join(
                " | ",
                result.Environments
                    .SelectMany(environment => environment.Endpoints)
                    .SelectMany(endpoint => endpoint.Tests)
                    .Where(test => !test.IsSuccess)
                    .Select(test => $"{test.Name}: {test.ErrorMessage ?? "unknown failure"}"));

            Assert.True(result.IsSuccess, failureSummary);
            Assert.NotNull(handler.CapturedRequest);
            Assert.Equal(
                "https://localhost:7001/api/reports/C1001?month=03&year=2026&previousDay=2026-03-13",
                handler.CapturedRequest!.RequestUri!.ToString());
            Assert.Equal("Bearer", handler.CapturedRequest.Headers.Authorization?.Scheme);
            Assert.Equal("secret-token", handler.CapturedRequest.Headers.Authorization?.Parameter);

            var bodyJson = JsonNode.Parse(handler.CapturedBody!);
            Assert.Equal("2026-03", bodyJson?["period"]?.GetValue<string>());
            Assert.Equal("C1001", bodyJson?["customerId"]?.GetValue<string>());
            Assert.Equal("2026-03-14", bodyJson?["generatedOn"]?.GetValue<string>());
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, originalEnvVar);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? CapturedRequest { get; set; }

        public string? CapturedBody { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _localNow;

        public FixedTimeProvider(DateTimeOffset localNow)
        {
            _localNow = localNow;
        }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone(
            "Fixed",
            _localNow.Offset,
            "Fixed",
            "Fixed");

        public override DateTimeOffset GetUtcNow()
        {
            return _localNow.ToUniversalTime();
        }
    }
}
