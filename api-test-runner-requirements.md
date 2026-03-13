
# YAML‑Driven API Test Runner – Requirements Specification

## 1. Overview

This document defines the requirements for a **YAML‑driven API testing tool** implemented in **.NET**.

The application allows developers to define API test cases using **YAML configuration files** and run those tests using a **.NET executable**. Test results are displayed through a **local web dashboard** launched automatically when the tool runs.

The goal is to provide a lightweight, configurable API test runner similar to Postman/Newman but fully scriptable through YAML and executable locally.

---

# 2. Objectives

The system must:

- Execute API tests defined in YAML files
- Support testing multiple APIs across multiple environments
- Allow validation of response fields including:
  - strings
  - objects
  - arrays of objects
- Provide a **web dashboard** showing test status
- Run from a **single .NET executable**
- Allow tests to be written **without writing code**
- Allow configuration through `appsettings.json`

---

# 3. System Architecture

## 3.1 High-Level Architecture

Executable (.NET)
    |
    ├── YAML Loader
    |
    ├── Test Execution Engine
    |       ├── HTTP Request Builder
    |       ├── Assertion Engine
    |       └── Result Collector
    |
    └── Web Dashboard (ASP.NET Core)
            └── Displays test results

---

# 4. Functional Requirements

## 4.1 YAML Test Definitions

Test cases must be defined in YAML files.

The runner will:

1. Load YAML configuration
2. Build HTTP requests
3. Execute requests
4. Validate responses
5. Display results

---

# 5. Environment Configuration

The YAML must support multiple environments.

Example:

```yaml
environments:
  - name: Local
    baseUrl: https://localhost:7001
  - name: UAT
    baseUrl: https://uat-api.company.com
```

Each environment defines a **base URL** used by its endpoints.

---

# 6. Endpoint Definition

Each environment can contain multiple endpoints.

Example:

```yaml
endpoints:
  - name: Get Accounts
    method: GET
    path: /api/accounts
```

Required fields:

| Field | Description |
|------|-------------|
| name | Display name |
| method | HTTP method |
| path | API path |

Supported methods:

GET, POST, PUT, PATCH, DELETE

---

# 7. Request Parameters

## 7.1 Path Parameters

```yaml
path: /api/customers/{customerId}

pathParams:
  customerId: C1001
```

---

## 7.2 Query Parameters

```yaml
query:
  page: 1
  pageSize: 50
```

Produces:

`/api/accounts?page=1&pageSize=50`

---

## 7.3 Headers

```yaml
headers:
  Authorization: Bearer token
  Content-Type: application/json
```

---

## 7.4 JSON Body

```yaml
body:
  customerId: C1001
  currency: SGD
  items:
    - productCode: FUND1
      amount: 1000
```

---

# 8. Test Definitions

Each endpoint can contain one or more tests.

```yaml
tests:
  - name: Accounts should exist
    expectedStatus: 200
```

---

# 9. Assertions

Assertions validate response fields.

Field paths use dot notation.

Example:

`data.accounts[0].accountNo`

Example assertion:

```yaml
assertions:
  - field: success
    equals: true

  - field: message
    containsText: success

  - field: data.accounts
    type: array
    minCount: 1
```

---

# 10. Supported Assertion Types

### Equality

```yaml
equals: value
```

### Not Equals

```yaml
notEquals: value
```

### Type Checking

Supported types:

- string
- number
- boolean
- object
- array

Example:

```yaml
type: object
```

---

### String Assertions

Supported:

- containsText
- startsWith
- endsWith
- notEmpty

Example:

```yaml
containsText: success
```

---

### Array Assertions

Supported:

- minCount
- maxCount
- count

Example:

```yaml
minCount: 1
```

---

### Array Object Matching

Example:

```yaml
assertions:
  - field: data.accounts
    contains:
      status: Active
```

Meaning at least one element in the array must match.

---

# 11. Sample YAML Configuration

```yaml
environments:
  - name: Local
    baseUrl: https://localhost:7001

    endpoints:

      - name: Login
        method: POST
        path: /api/auth/login

        headers:
          Content-Type: application/json

        body:
          username: testuser
          password: password123

        tests:
          - name: Login should succeed
            expectedStatus: 200

            assertions:
              - field: success
                equals: true

              - field: data.token
                type: string
                notEmpty: true

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

              - field: data.accounts
                contains:
                  status: Active
```

---

# 12. Web Dashboard

When the executable starts:

1. Local web server starts
2. Browser opens automatically

Example URL:

http://localhost:5005

Dashboard displays:

- environments
- endpoints
- pass/fail status
- error messages
- execution time

Example:

Environment: Local

✔ Login  
✔ Get Accounts

Summary  
Total Tests: 2  
Passed: 2  
Failed: 0

---

# 13. Web Server Configuration

The web dashboard port must be configurable through `appsettings.json`.

Example:

```json
{
  "WebServer": {
    "Host": "localhost",
    "Port": 5005,
    "AutoLaunchBrowser": true
  }
}
```

Configuration fields:

| Field | Description |
|------|-------------|
| Host | Host binding |
| Port | Web dashboard port |
| AutoLaunchBrowser | Automatically open browser |

---

# 14. Execution Flow

Load YAML  
↓  
Initialize environment  
↓  
Build HTTP request  
↓  
Execute API call  
↓  
Parse response JSON  
↓  
Run assertions  
↓  
Store results  
↓  
Display dashboard

---

# 15. Technology Stack

| Component | Technology |
|----------|------------|
| Runtime | .NET 8 |
| YAML Parser | YamlDotNet |
| HTTP Client | HttpClient |
| JSON Parsing | System.Text.Json |
| Web Dashboard | ASP.NET Core |
| Frontend | HTML + JavaScript |

---

# 16. Non-Functional Requirements

Performance:

- Must support concurrent endpoint execution

Logging:

Must log:

- requests
- responses
- assertion failures

Extensibility:

Future features should support authentication flows, retries, CI/CD integration, and scheduling.

---

# 17. Future Enhancements

These features are **not required for the first version** but should be considered for future development.

### Live Test Progress Updates

Real-time dashboard updates using SignalR or WebSockets.

### CLI Environment Selection

Example:

ApiTestRunner.exe --env UAT

### Test Filtering

Run specific tests by tag.

Example:

ApiTestRunner.exe --tag smoke

### Variable Capture

Extract values from responses.

Example:

capture:
  token: data.token

Reuse:

Authorization: Bearer {{token}}

### Parallel Execution

Allow configuration such as:

parallel: true  
maxConcurrency: 5

### CI/CD Integration

Support CI mode:

ApiTestRunner.exe --ci

Output formats:

- JSON
- JUnit XML
- HTML

### Scheduled Test Execution

Support automated scheduled health checks.

### Enhanced Reporting

Historical reports, response time tracking, and trend analysis.

### Authentication Providers

Support OAuth2, JWT acquisition, and API key flows.

### Plugin Architecture

Allow custom assertions and request processors.

---

# 18. Success Criteria

The tool is considered successful when:

- Tests can be written purely in YAML
- No code changes are required to add new endpoints
- Test results are visible via the dashboard
- Developers can run the tests with a single executable

