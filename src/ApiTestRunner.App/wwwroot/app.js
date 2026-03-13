const runButton = document.getElementById("runButton");
const refreshButton = document.getElementById("refreshButton");
const environmentContainer = document.getElementById("environmentContainer");
const environmentTemplate = document.getElementById("environmentTemplate");
const endpointTemplate = document.getElementById("endpointTemplate");
const testTemplate = document.getElementById("testTemplate");

async function fetchState() {
    const response = await fetch("/api/dashboard/state", { cache: "no-store" });
    if (!response.ok) {
        throw new Error(`Dashboard request failed with status ${response.status}`);
    }

    return response.json();
}

async function runSuite() {
    setBusy(true);

    try {
        const response = await fetch("/api/dashboard/run", { method: "POST" });
        if (!response.ok) {
            throw new Error(`Run request failed with status ${response.status}`);
        }

        const state = await response.json();
        renderState(state);
    } catch (error) {
        renderError(error);
    } finally {
        setBusy(false);
    }
}

function renderState(state) {
    const run = state.lastRun;

    document.getElementById("runStatus").textContent = buildStatusText(state);
    document.getElementById("startedAt").textContent = `Started: ${formatDate(state.lastStartedAtUtc)}`;
    document.getElementById("completedAt").textContent = `Completed: ${formatDate(state.lastCompletedAtUtc)}`;

    document.getElementById("totalTests").textContent = run ? run.totalTests : "-";
    document.getElementById("passedTests").textContent = run ? run.passedTests : "-";
    document.getElementById("failedTests").textContent = run ? run.failedTests : "-";
    document.getElementById("totalDuration").textContent = run ? `${Math.round(run.totalDurationMs)} ms` : "-";

    environmentContainer.innerHTML = "";

    if (!run || !run.environments || run.environments.length === 0) {
        environmentContainer.innerHTML = `
            <section class="empty-state">
                <h3>No run results yet</h3>
                <p>Use the Run Tests button to execute the YAML suite and populate the dashboard.</p>
            </section>`;
        return;
    }

    for (const environment of run.environments) {
        const environmentNode = environmentTemplate.content.firstElementChild.cloneNode(true);
        environmentNode.querySelector(".environment-name").textContent = environment.name;
        environmentNode.querySelector(".environment-url").textContent = environment.baseUrl;

        const environmentBadge = environmentNode.querySelector(".environment-badge");
        environmentBadge.textContent = environment.failedTests === 0 ? "Passing" : "Issues";
        environmentBadge.className = `environment-badge ${environment.failedTests === 0 ? "passing" : "failing"}`;

        const endpointList = environmentNode.querySelector(".endpoint-list");

        for (const endpoint of environment.endpoints) {
            const endpointNode = endpointTemplate.content.firstElementChild.cloneNode(true);
            endpointNode.querySelector(".endpoint-name").textContent = endpoint.name;
            endpointNode.querySelector(".endpoint-meta").textContent =
                `${endpoint.method} ${endpoint.requestUrl} - ${Math.round(endpoint.durationMs)} ms`;

            const endpointBadge = endpointNode.querySelector(".endpoint-badge");
            endpointBadge.textContent = endpoint.isSuccess ? "Pass" : "Fail";
            endpointBadge.className = `endpoint-badge ${endpoint.isSuccess ? "passing" : "failing"}`;

            endpointNode.querySelector(".response-body").textContent =
                endpoint.responseBody || endpoint.errorMessage || "(empty response)";

            const testList = endpointNode.querySelector(".test-list");

            for (const test of endpoint.tests) {
                const testNode = testTemplate.content.firstElementChild.cloneNode(true);
                testNode.querySelector(".test-name").textContent = test.name;

                const testBadge = testNode.querySelector(".test-badge");
                testBadge.textContent = test.isSuccess ? "Pass" : "Fail";
                testBadge.className = `test-badge ${test.isSuccess ? "passing" : "failing"}`;

                const expectedText = `Expected ${test.expectedStatus}, actual ${test.actualStatus ?? "n/a"}`;
                const errorSuffix = test.errorMessage ? ` - ${test.errorMessage}` : "";
                testNode.querySelector(".test-status-line").textContent = `${expectedText}${errorSuffix}`;

                const assertionList = testNode.querySelector(".assertion-list");
                if (test.assertions.length === 0) {
                    assertionList.innerHTML = "<li>No assertions configured.</li>";
                } else {
                    for (const assertion of test.assertions) {
                        const listItem = document.createElement("li");
                        listItem.className = assertion.isSuccess ? "assertion-pass" : "assertion-fail";
                        listItem.textContent = `${assertion.rule} on ${assertion.field}: ${assertion.message}`;
                        assertionList.appendChild(listItem);
                    }
                }

                testList.appendChild(testNode);
            }

            endpointList.appendChild(endpointNode);
        }

        environmentContainer.appendChild(environmentNode);
    }
}

function buildStatusText(state) {
    if (state.isRunning) {
        return "Tests are running.";
    }

    if (state.lastError) {
        return `Last run failed before completion: ${state.lastError}`;
    }

    if (!state.lastRun) {
        return "Waiting for the first test run.";
    }

    return state.lastRun.failedTests === 0
        ? `Last run passed with ${state.lastRun.passedTests} successful tests.`
        : `Last run completed with ${state.lastRun.failedTests} failing tests.`;
}

function renderError(error) {
    document.getElementById("runStatus").textContent = error.message || "An unexpected dashboard error occurred.";
}

function formatDate(value) {
    if (!value) {
        return "-";
    }

    return new Date(value).toLocaleString();
}

function setBusy(isBusy) {
    runButton.disabled = isBusy;
    refreshButton.disabled = isBusy;
    runButton.textContent = isBusy ? "Running..." : "Run Tests";
}

runButton.addEventListener("click", runSuite);
refreshButton.addEventListener("click", async () => {
    try {
        renderState(await fetchState());
    } catch (error) {
        renderError(error);
    }
});

fetchState()
    .then(renderState)
    .catch(renderError);
