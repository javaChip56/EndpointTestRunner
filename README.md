# API Test Runner

`ApiTestRunner` is a production-ready .NET 8 solution for executing YAML-defined API tests and viewing the latest pass/fail snapshot in a local ASP.NET Core dashboard.

## Project structure

- `src/ApiTestRunner.App` hosts the executable, dashboard UI, sample APIs, configuration, and startup automation.
- `src/ApiTestRunner.Core` contains YAML loading, HTTP execution, JSON assertion evaluation, merge logic, and result models.
- `src/ApiTestRunner.App/Samples` contains the runnable split-file sample suite used by the app.
- `samples` mirrors the split sample structure at the repository root for easier browsing.

## Features

- Multiple environments in YAML
- Multiple endpoints per base URL
- One YAML file per endpoint when preferred
- Shared environment definition files
- Exact file paths, directories, and glob patterns in `Execution.TestFiles`
- Path params, query params, headers, and JSON request bodies
- Dot-notation assertions with array index support
- Validation for strings, objects, and arrays
- Pass/fail reporting with response previews in the dashboard
- Configurable dashboard host, port, browser auto-launch, suite files, and concurrency through `appsettings.json`

## Requirements summary

- The runner loads YAML definitions, builds HTTP requests with `HttpClient`, parses YAML with `YamlDotNet`, evaluates JSON assertions with `System.Text.Json`, and serves a local dashboard with ASP.NET Core.
- The dashboard host, port, and browser auto-launch behavior come from `appsettings.json`.
- Multiple environments and multiple endpoints per environment are supported.
- Assertions can target string, object, and array fields using dot notation and array indexes.

## Run

From the repository root:

```powershell
dotnet restore ApiTestRunner.sln
dotnet run --project src/ApiTestRunner.App -c Release
```

The app starts the dashboard at `http://localhost:5005` by default, auto-launches the browser if enabled, and executes the split sample suite after the web server is ready.

## CI/CD

The repository now includes a GitHub Actions based CI/CD setup under [`.github/workflows`](D:/Projects/Research/EndpointTestRunner/.github/workflows):

- `ci.yml` restores, builds, runs automated tests, and uploads test results plus coverage output.
- `sast.yml` runs GitHub CodeQL for static application security testing.
- `release.yml` validates the solution, publishes self-contained release builds for `win-x64` and `linux-x64`, and attaches zip artifacts to GitHub releases for tags matching `v*`.

Release examples:

- `v1.0.0`
- `v1.1.0`

Assumption:

- The CI/CD platform is GitHub Actions. If you need Azure DevOps, GitLab CI, or Jenkins instead, the same stages can be ported.

## Configuration

`src/ApiTestRunner.App/appsettings.json` controls:

- `WebServer.Host`
- `WebServer.Port`
- `WebServer.AutoLaunchBrowser`
- `Execution.TestFiles`
- `Execution.MaxConcurrency`
- `Execution.HttpTimeoutSeconds`

`Execution.TestFiles` accepts:

- Exact file paths
- Directory paths
- Glob patterns such as `Samples/Endpoints/**/*.yaml`

Example:

```json
{
  "Execution": {
    "TestFiles": [
      "Samples/Environments/**/*.yaml",
      "Samples/Endpoints/**/*.yaml"
    ]
  }
}
```

## Supported YAML styles

Full suite file:

```yaml
environments:
  - name: Local
    baseUrl: http://localhost:5005
    endpoints:
      - name: Get Accounts
        method: GET
        path: /api/accounts
        tests:
          - name: Accounts should exist
            expectedStatus: 200
```

Shared environment file:

```yaml
environments:
  - name: Local
    baseUrl: http://localhost:5005
  - name: UAT
    baseUrl: https://uat-api.company.com
```

Endpoint-only file:

```yaml
targetEnvironments:
  - Local
  - UAT

endpoints:
  - name: Get Accounts
    method: GET
    path: /api/accounts
    query:
      customerId: C1001
    tests:
      - name: Accounts should exist
        expectedStatus: 200
        assertions:
          - field: data.accounts
            type: array
            minCount: 1
```

If only one environment is defined across all loaded YAML files, `targetEnvironments` can be omitted for endpoint-only files and the endpoint is attached to that single environment automatically.

## Supported assertion keywords

The runner currently supports these assertion keys:

- `equals`
- `notEquals`
- `type`
- `containsText`
- `startsWith`
- `endsWith`
- `notEmpty`
- `minCount`
- `maxCount`
- `count`
- `contains`

Supported `type` values:

- `string`
- `number`
- `boolean`
- `object`
- `array`

Example:

```yaml
assertions:
  - field: success
    equals: true

  - field: message
    containsText: success

  - field: data.token
    type: string
    notEmpty: true

  - field: data.accounts
    type: array
    minCount: 1

  - field: data.accounts
    contains:
      status: Active
```

## Recommended split layout

```text
Samples/
  Environments/
    sample-api.yaml
  Endpoints/
    auth/
      login.yaml
    accounts/
      get-accounts.yaml
    customers/
      get-customer-details.yaml
```

## Merge rules

- Environment definitions from different files are merged by environment name.
- If the same environment name appears with different `baseUrl` values, loading fails fast.
- Full-suite files and endpoint-only files can be mixed in the same run.
- Endpoint-only files are attached to the environments named in `targetEnvironments`.

## Assumptions

- Different endpoints can return completely different response schemas, and assertions are evaluated against each endpoint's own response.
- In the current version, each endpoint request is executed once per run and multiple tests defined under that same endpoint validate that single execution result.
- Assertions treat dot notation as object traversal and `[index]` as array access.
- `notEmpty` also works for objects and arrays to keep the first version practical.
- The sample suite targets the embedded sample API so the repository can run locally without depending on a third-party service.
