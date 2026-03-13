# API Test Runner

`ApiTestRunner` is a production-ready .NET 8 solution for executing YAML-defined API tests and viewing the latest pass/fail snapshot in a local ASP.NET Core dashboard.

## Project structure

- `src/ApiTestRunner.App` hosts the executable, dashboard UI, sample APIs, configuration, and startup automation.
- `src/ApiTestRunner.Core` contains YAML loading, HTTP execution, JSON assertion evaluation, and result models.
- `samples/sample-api-tests.yaml` is a runnable sample suite that targets the embedded sample API.

## Features

- Multiple environments in YAML
- Multiple endpoints per base URL
- Path params, query params, headers, and JSON request bodies
- Dot-notation assertions with array index support
- Validation for strings, objects, and arrays
- Pass/fail reporting with response previews in the dashboard
- Configurable dashboard host, port, browser auto-launch, suite files, and concurrency through `appsettings.json`

## Requirements

- .NET SDK 8.0 or later

## Run

From the repository root:

```powershell
dotnet restore
dotnet run --project src/ApiTestRunner.App
```

The app starts the dashboard at `http://localhost:5005` by default, auto-launches the browser if enabled, and executes the sample suite after the web server is ready.

## Configuration

`src/ApiTestRunner.App/appsettings.json` controls:

- `WebServer.Host`
- `WebServer.Port`
- `WebServer.AutoLaunchBrowser`
- `Execution.TestFiles`
- `Execution.MaxConcurrency`
- `Execution.HttpTimeoutSeconds`

## YAML shape

The supported YAML structure is:

```yaml
environments:
  - name: Local
    baseUrl: http://localhost:5005
    endpoints:
      - name: Example
        method: GET
        path: /api/example/{id}
        pathParams:
          id: 42
        query:
          page: 1
        headers:
          Authorization: Bearer token
        body:
          sample: true
        tests:
          - name: Should work
            expectedStatus: 200
            assertions:
              - field: data.items[0].name
                type: string
                notEmpty: true
```

## Assumptions

- Different endpoints can return completely different response schemas, and assertions are evaluated against each endpoint's own response.
- In the current version, each endpoint request is executed once per run and multiple tests defined under that same endpoint validate that single execution result.
- Assertions treat dot notation as object traversal and `[index]` as array access.
- `notEmpty` also works for objects and arrays to keep the first version practical.
- The sample suite targets the embedded sample API so the repository can run locally without depending on a third-party service.
