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
- Local AdminLTE-based dashboard shell with vendored static assets under `wwwroot/lib`
- Exact file paths, directories, and glob patterns in `Execution.TestFiles`
- Path params, query params, headers, and JSON request bodies
- Dot-notation assertions with array index support
- Validation for strings, objects, and arrays
- CLI and CI execution mode with environment and file filtering, JSON output, JUnit XML output, and non-zero exit codes on failed runs
- Toggleable test selection in the dashboard, including select-all, clear-all, expand-all, collapse-all, and individual test control
- Collapsible pass/fail reporting with environment, endpoint, test, and response-preview drill-down
- cURL analysis page that scans configured YAML definitions, generates suggested environment and endpoint YAML when missing, and supports response-driven assertion building
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

## CLI and CI mode

The same executable also supports headless execution for terminal and pipeline use.

Basic examples:

```powershell
dotnet run --project src/ApiTestRunner.App -- --ci
dotnet run --project src/ApiTestRunner.App -- --ci --env Local
dotnet run --project src/ApiTestRunner.App -- --ci --env Local,UAT
dotnet run --project src/ApiTestRunner.App -- --ci --file Samples/Environments/sample-api.yaml --file Samples/Endpoints/accounts/get-accounts.yaml
dotnet run --project src/ApiTestRunner.App -- --ci --format json
dotnet run --project src/ApiTestRunner.App -- --ci --format junit --output artifacts/test-results.xml
```

Supported CLI options:

- `--ci` enables headless execution mode
- `--env <name[,name2]>` runs only the named environments and can be repeated
- `--file <path|glob|directory>` overrides `Execution.TestFiles` for this run and can be repeated
- `--format <none|json|junit>` emits machine-readable results
- `--output <path>` writes formatted output to a file instead of standard output

CLI behavior:

- exit code `0` means all selected tests passed
- exit code `1` means at least one test failed or the run could not be completed
- relative `--file` and `--output` paths are resolved from the app content root
- `--file` accepts the same exact paths, directories, and glob patterns as `Execution.TestFiles`
- when `--format` is omitted, the CLI still prints the human-readable execution summary

Practical note:

- CLI mode still boots the ASP.NET Core host internally before running the suite. That is intentional because the bundled sample suite targets the embedded `/sample-api/*` endpoints.

## Dashboard workflow

The main dashboard now exposes a test-selection panel before execution:

- `Select All` enables the full suite.
- `Clear All` disables every test.
- `Expand All` and `Collapse All` control the selection tree.
- Search filters APIs, base URLs, endpoints, methods, and test names in the selection tree.
- The results view also has live search for APIs, endpoints, tests, assertions, and error text.
- Matching text is highlighted in both selection and result sections to make hits easier to spot.
- Environment, endpoint, and individual test checkboxes let you run only the subset you care about.
- Each endpoint row in test selection also exposes an `Edit endpoint` action that opens the cURL import page preloaded with that endpoint's current request and tests.
- Each page load starts with all tests selected by default.
- The dashboard and cURL import pages use local AdminLTE assets, so the UI does not rely on external CDNs at runtime.

The results view also supports:

- `Expand All` and `Collapse All` for the latest result set
- collapsible environment, endpoint, and test sections
- per-environment passed/failed/total counts
- response preview panels per endpoint

Important note:

- The runner still executes one HTTP request per endpoint. Selecting one test under an endpoint will run that endpoint once and evaluate only the selected tests against that shared response.

## cURL import workflow

Open `http://localhost:5005/curl-import.html` to paste a cURL command and inspect it.

The tool will:

- parse the request URL, method, headers, query string, and JSON body
- optionally parse a pasted JSON response body in the same flow so the assertion builder stays on the same page
- let you create multiple drafted tests, each with its own expected status and assertion set, then use `Analyze and Generate` to include all of them in the endpoint YAML preview
- open directly from the dashboard's `Edit endpoint` action so existing endpoint requests and tests can be adjusted in the importer flow
- save an edited endpoint back to its owning endpoint YAML file with `Save Endpoint YAML`
- scan the configured YAML suite for an existing environment whose `baseUrl` already covers the pasted request URL
- scan for an existing endpoint with the same method and either the same relative path or a matching path template such as `/customers/{customerId}`
- generate suggested environment YAML when the base URL is not already present
- generate suggested endpoint YAML when the endpoint is not already present
- accept a pasted JSON response body so you can pick response fields and build assertion YAML into the generated endpoint test
- let you copy generated environment or endpoint YAML directly to the clipboard
- continue generating suggestions even when configured YAML files are missing, while showing a warning instead of failing the whole cURL import flow

Current first-version scope:

- best support is for common `curl`, `-X`, `-H`, `--data`, `--data-raw`, `--data-binary`, and `--url` forms
- the page now uses one main `Analyze and Generate` action instead of separate request-analysis and response-parse steps
- each drafted test in the cURL page keeps its own assertions, so you can generate multiple YAML tests for one endpoint in a single pass
- generated YAML is shown as preview text, not written directly to disk
- when editing an existing endpoint from the dashboard, the cURL importer can now write the updated endpoint back to its original YAML file
- the assertion builder currently targets the most common field assertions: `equals`, `notEquals`, `type`, `containsText`, `startsWith`, `endsWith`, `notEmpty`, `greaterThan`, `greaterThanOrEqual`, `lessThan`, `lessThanOrEqual`, `minCount`, `maxCount`, and `count`
- missing YAML warnings are surfaced in the cURL import UI, but malformed YAML content still needs to be fixed before the dashboard runner can execute the suite

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

Pipeline example:

```powershell
dotnet run --project src/ApiTestRunner.App -- --ci --env Local --format junit --output artifacts/test-results.xml
```

This is a good default shape for CI jobs because it produces a predictable exit code and a result artifact that most pipeline systems can ingest.

## Future enhancements

The current version is intentionally focused on a local dashboard-driven workflow. The next major enhancements are:

- Response value capture and run-scoped variable reuse so one request can extract data such as tokens or IDs and pass them into later requests
- Live execution progress in the dashboard so long-running suites can stream environment, endpoint, and test updates while the run is still in progress

## Configuration

`src/ApiTestRunner.App/appsettings.json` controls:

- `WebServer.Host`
- `WebServer.Port`
- `WebServer.AutoLaunchBrowser`
- `Execution.TestFiles`
- `Execution.MaxConcurrency`
- `Execution.HttpTimeoutSeconds`

CLI mode can override part of this configuration per run:

- `--file` overrides `Execution.TestFiles`
- `--env` narrows execution to specific loaded environments
- `--format` and `--output` affect only CLI result export and do not change dashboard behavior

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
- `greaterThan`
- `greaterThanOrEqual`
- `lessThan`
- `lessThanOrEqual`
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

  - field: data.totalRowsCount
    greaterThan: 0

  - field: data.accounts
    contains:
      status: Active

  - field: data.accounts
    contains:
      portfolioValue:
        greaterThan: 1000
      status: Active
```

## Dynamic parameters and variables

The runner supports token-based variable resolution for request values, so YAML does not need to hard-code values like month, year, dates, secrets, or shared IDs.

Tokens can be used in:

- `baseUrl`
- `path`
- `pathParams`
- `query`
- `headers`
- `body`
- assertion values
- environment `variables`

Supported token providers:

- `{{now:FORMAT}}`
- `{{now:OFFSET:FORMAT}}`
- `{{today:FORMAT}}`
- `{{today:OFFSET:FORMAT}}`
- `{{var:VariableName}}`
- `{{env:ENVIRONMENT_VARIABLE_NAME}}`
- `{{config:Configuration.Key}}`

Supported date offset units:

- `d` for days
- `M` for months
- `y` for years
- `h` for hours
- `m` for minutes

Examples:

```yaml
environments:
  - name: UAT
    baseUrl: https://api.company.com
    variables:
      reportYear: "{{now:yyyy}}"
      reportMonth: "{{now:MM}}"

endpoints:
  - name: Monthly Report
    method: POST
    path: /api/reports/{customerId}
    pathParams:
      customerId: "{{config:Variables.DefaultCustomerId}}"
    query:
      month: "{{var:reportMonth}}"
      year: "{{var:reportYear}}"
      previousDay: "{{today:-1d:yyyy-MM-dd}}"
    headers:
      Authorization: Bearer {{env:API_TOKEN}}
    body:
      period: "{{var:reportYear}}-{{var:reportMonth}}"
      generatedOn: "{{today:yyyy-MM-dd}}"
```

Notes:

- A token can occupy the whole value or be embedded inside a larger string.
- Environment variables can reference other environment variables.
- `config:` reads from application configuration, so `config:Variables.DefaultCustomerId` maps to `Variables:DefaultCustomerId`.
- Assertions support tokens in `field`, `equals`, `notEquals`, `type`, `containsText`, `startsWith`, `endsWith`, `contains`, `notEmpty`, `greaterThan`, `greaterThanOrEqual`, `lessThan`, `lessThanOrEqual`, `minCount`, `maxCount`, and `count`.

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
