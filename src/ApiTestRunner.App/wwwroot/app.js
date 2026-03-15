const runButton = document.getElementById("runButton");
const refreshButton = document.getElementById("refreshButton");
const selectAllButton = document.getElementById("selectAllButton");
const clearAllButton = document.getElementById("clearAllButton");
const expandSelectionButton = document.getElementById("expandSelectionButton");
const collapseSelectionButton = document.getElementById("collapseSelectionButton");
const expandResultsButton = document.getElementById("expandResultsButton");
const collapseResultsButton = document.getElementById("collapseResultsButton");
const selectionSearchInput = document.getElementById("selectionSearchInput");
const resultsSearchInput = document.getElementById("resultsSearchInput");
const selectionContainer = document.getElementById("selectionContainer");
const selectionSummary = document.getElementById("selectionSummary");
const resultsSummary = document.getElementById("resultsSummary");
const environmentContainer = document.getElementById("environmentContainer");
const environmentTemplate = document.getElementById("environmentTemplate");
const endpointTemplate = document.getElementById("endpointTemplate");
const testTemplate = document.getElementById("testTemplate");

let suiteManifest = null;
let selectedTestIds = new Set();
let lastRunState = null;
let selectionSearchTerm = "";
let resultsSearchTerm = "";

const selectionExpansionState = {
    environments: new Map(),
    endpoints: new Map()
};

const resultExpansionState = {
    environments: new Map(),
    endpoints: new Map(),
    tests: new Map()
};

async function fetchState() {
    const response = await fetch("/api/dashboard/state", { cache: "no-store" });
    if (!response.ok) {
        throw new Error(await buildErrorMessage(response, "Dashboard request failed"));
    }

    return response.json();
}

async function fetchManifest() {
    const response = await fetch("/api/dashboard/manifest", { cache: "no-store" });
    if (!response.ok) {
        throw new Error(await buildErrorMessage(response, "Manifest request failed"));
    }

    return response.json();
}

async function initializeDashboard() {
    setBusy(true);

    try {
        const [manifest, state] = await Promise.all([fetchManifest(), fetchState()]);
        suiteManifest = manifest;
        hydrateSelection(manifest);
        renderSelection(manifest);
        renderState(state);
    } catch (error) {
        renderError(error);
    } finally {
        setBusy(false);
    }
}

async function runSuite() {
    if (!suiteManifest) {
        renderError(new Error("The test manifest has not loaded yet."));
        return;
    }

    const allTestIds = getAllTestIds(suiteManifest);
    if (selectedTestIds.size === 0) {
        renderError(new Error("Select at least one test before running the suite."));
        return;
    }

    setBusy(true);

    try {
        const response = await fetch("/api/dashboard/run", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                runAll: selectedTestIds.size === allTestIds.length,
                selectedTestIds: Array.from(selectedTestIds)
            })
        });

        if (!response.ok) {
            throw new Error(await buildErrorMessage(response, "Run request failed"));
        }

        const state = await response.json();
        renderState(state);
    } catch (error) {
        renderError(error);
    } finally {
        setBusy(false);
    }
}

function hydrateSelection(manifest) {
    selectedTestIds = new Set(getAllTestIds(manifest));

    for (const environment of manifest.environments) {
        selectionExpansionState.environments.set(environment.id, true);

        for (const endpoint of environment.endpoints) {
            selectionExpansionState.endpoints.set(endpoint.id, false);
        }
    }
}

function renderSelection(manifest) {
    selectionContainer.innerHTML = "";
    const searchDisplayTerm = selectionSearchInput.value.trim();

    if (!manifest || !manifest.environments || manifest.environments.length === 0) {
        selectionSummary.textContent = "No tests were found in the configured YAML files.";
        selectionContainer.innerHTML = "<p class=\"empty-selection\">No selectable tests were loaded.</p>";
        updateSelectionButtons(false);
        return;
    }

    const totalTestCount = manifest.totalTests;
    const filteredEnvironments = filterManifestEnvironments(manifest, selectionSearchTerm);
    const visibleEndpointCount = filteredEnvironments.reduce((count, environmentEntry) => count + environmentEntry.endpoints.length, 0);
    const visibleTestCount = filteredEnvironments.reduce(
        (count, environmentEntry) => count + environmentEntry.endpoints.reduce((endpointCount, endpointEntry) => endpointCount + endpointEntry.tests.length, 0),
        0
    );

    selectionSummary.textContent = selectionSearchTerm
        ? `${selectedTestIds.size} of ${totalTestCount} tests selected | ${visibleEndpointCount} endpoints and ${visibleTestCount} tests shown for "${searchDisplayTerm}"`
        : `${selectedTestIds.size} of ${totalTestCount} tests selected`;
    updateSelectionButtons(true);

    if (filteredEnvironments.length === 0) {
        selectionContainer.innerHTML = `<p class="empty-selection">No APIs or endpoints match "${escapeHtml(searchDisplayTerm)}".</p>`;
        return;
    }

    for (const environmentEntry of filteredEnvironments) {
        const { environment, environmentMatches, endpoints } = environmentEntry;
        const visibleEnvironmentTestIds = endpoints.flatMap((endpointEntry) => endpointEntry.tests.map((test) => test.id));

        const environmentNode = document.createElement("details");
        environmentNode.className = "selection-group";
        environmentNode.open = selectionSearchTerm ? true : selectionExpansionState.environments.get(environment.id) ?? true;
        environmentNode.addEventListener("toggle", () => {
            selectionExpansionState.environments.set(environment.id, environmentNode.open);
        });

        const environmentSummary = document.createElement("summary");
        environmentSummary.className = "selection-summary-row";
        environmentSummary.appendChild(createSelectionHeader(
            highlightMatch(environment.name, selectionSearchTerm),
            environmentMatches || !selectionSearchTerm
                ? `${highlightMatch(environment.baseUrl, selectionSearchTerm)} - ${environment.totalTests} tests`
                : `${highlightMatch(environment.baseUrl, selectionSearchTerm)} - ${visibleEnvironmentTestIds.length} matching tests`,
            visibleEnvironmentTestIds,
            toggleGroupSelection
        ));

        const environmentBody = document.createElement("div");
        environmentBody.className = "selection-group-body";

        for (const endpointEntry of endpoints) {
            const { endpoint, endpointMatches, tests } = endpointEntry;
            const endpointIds = tests.map((test) => test.id);

            const endpointNode = document.createElement("details");
            endpointNode.className = "selection-subgroup";
            endpointNode.open = selectionSearchTerm ? true : selectionExpansionState.endpoints.get(endpoint.id) ?? false;
            endpointNode.addEventListener("toggle", () => {
                selectionExpansionState.endpoints.set(endpoint.id, endpointNode.open);
            });

            const endpointSummary = document.createElement("summary");
            endpointSummary.className = "selection-summary-row";
            endpointSummary.appendChild(createSelectionHeader(
                highlightMatch(endpoint.name, selectionSearchTerm),
                endpointMatches || !selectionSearchTerm
                    ? `${highlightMatch(endpoint.method, selectionSearchTerm)} ${highlightMatch(endpoint.path, selectionSearchTerm)} - ${tests.length} tests`
                    : `${highlightMatch(endpoint.method, selectionSearchTerm)} ${highlightMatch(endpoint.path, selectionSearchTerm)} - ${tests.length} matching tests`,
                endpointIds,
                toggleGroupSelection
            ));

            const testList = document.createElement("div");
            testList.className = "selection-test-list";

            for (const test of tests) {
                const testRow = document.createElement("label");
                testRow.className = "selection-test";

                const checkbox = document.createElement("input");
                checkbox.type = "checkbox";
                checkbox.checked = selectedTestIds.has(test.id);
                checkbox.addEventListener("change", () => toggleTestSelection(test.id, checkbox.checked));

                const details = document.createElement("span");
                details.className = "selection-label-stack";
                details.innerHTML = `<strong>${highlightMatch(test.name, selectionSearchTerm)}</strong><span>Expected HTTP ${test.expectedStatus}</span>`;

                testRow.appendChild(checkbox);
                testRow.appendChild(details);
                testList.appendChild(testRow);
            }

            endpointNode.appendChild(endpointSummary);
            endpointNode.appendChild(testList);
            environmentBody.appendChild(endpointNode);
        }

        environmentNode.appendChild(environmentSummary);
        environmentNode.appendChild(environmentBody);
        selectionContainer.appendChild(environmentNode);
    }
}

function createSelectionHeader(titleHtml, detailHtml, childTestIds, onToggle) {
    const header = document.createElement("div");
    header.className = "selection-header-row";

    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = childTestIds.length > 0 && childTestIds.every((testId) => selectedTestIds.has(testId));
    checkbox.indeterminate = !checkbox.checked && childTestIds.some((testId) => selectedTestIds.has(testId));
    checkbox.addEventListener("click", (event) => {
        event.stopPropagation();
    });
    checkbox.addEventListener("change", () => onToggle(childTestIds, checkbox.checked));

    const labelStack = document.createElement("span");
    labelStack.className = "selection-label-stack";
    labelStack.innerHTML = `<strong>${titleHtml}</strong><span>${detailHtml}</span>`;

    header.appendChild(checkbox);
    header.appendChild(labelStack);
    return header;
}

function toggleGroupSelection(testIds, isChecked) {
    for (const testId of testIds) {
        if (isChecked) {
            selectedTestIds.add(testId);
        } else {
            selectedTestIds.delete(testId);
        }
    }

    renderSelection(suiteManifest);
}

function toggleTestSelection(testId, isChecked) {
    if (isChecked) {
        selectedTestIds.add(testId);
    } else {
        selectedTestIds.delete(testId);
    }

    renderSelection(suiteManifest);
}

function getAllTestIds(manifest) {
    return manifest.environments.flatMap((environment) =>
        environment.endpoints.flatMap((endpoint) => endpoint.tests.map((test) => test.id))
    );
}

function filterManifestEnvironments(manifest, searchTerm) {
    if (!searchTerm) {
        return manifest.environments.map((environment) => ({
            environment,
            environmentMatches: false,
            endpoints: environment.endpoints.map((endpoint) => ({
                endpoint,
                endpointMatches: false,
                tests: endpoint.tests
            }))
        }));
    }

    return manifest.environments
        .map((environment) => {
            const environmentMatches =
                matchesSearch(environment.name, searchTerm) ||
                matchesSearch(environment.baseUrl, searchTerm);

            const endpoints = environment.endpoints
                .map((endpoint) => {
                    const endpointMatches = environmentMatches || endpointMatchesSelectionSearch(endpoint, searchTerm);
                    return {
                        endpoint,
                        endpointMatches,
                        tests: endpointMatches
                            ? endpoint.tests
                            : endpoint.tests.filter((test) => testMatchesSelectionSearch(test, searchTerm))
                    };
                })
                .filter((endpointEntry) => endpointEntry.endpointMatches || endpointEntry.tests.length > 0);

            if (!environmentMatches && endpoints.length === 0) {
                return null;
            }

            return {
                environment,
                environmentMatches,
                endpoints
            };
        })
        .filter((entry) => entry !== null);
}

function endpointMatchesSelectionSearch(endpoint, searchTerm) {
    return (
        matchesSearch(endpoint.name, searchTerm) ||
        matchesSearch(endpoint.method, searchTerm) ||
        matchesSearch(endpoint.path, searchTerm)
    );
}

function testMatchesSelectionSearch(test, searchTerm) {
    return matchesSearch(test.name, searchTerm);
}

function renderState(state) {
    lastRunState = state;
    const run = state.lastRun;
    const searchDisplayTerm = resultsSearchInput.value.trim();
    setStatusError(Boolean(state.lastError));

    document.getElementById("runStatus").textContent = buildStatusText(state);
    document.getElementById("startedAt").textContent = `Started: ${formatDate(state.lastStartedAtUtc)}`;
    document.getElementById("completedAt").textContent = `Completed: ${formatDate(state.lastCompletedAtUtc)}`;

    document.getElementById("totalTests").textContent = run ? run.totalTests : "-";
    document.getElementById("passedTests").textContent = run ? run.passedTests : "-";
    document.getElementById("failedTests").textContent = run ? run.failedTests : "-";
    document.getElementById("totalDuration").textContent = run ? `${Math.round(run.totalDurationMs)} ms` : "-";

    environmentContainer.innerHTML = "";

    if (!run || !run.environments || run.environments.length === 0) {
        resultsSummary.textContent = "Use the controls to expand or collapse the latest results.";
        updateResultButtons(false);
        environmentContainer.innerHTML = `
            <section class="empty-state">
                <h3>No run results yet</h3>
                <p>Use the Run Tests button to execute the YAML suite and populate the dashboard.</p>
            </section>`;
        return;
    }

    const filteredEnvironments = filterRunEnvironments(run, resultsSearchTerm);
    const visibleEndpointCount = filteredEnvironments.reduce((count, environmentEntry) => count + environmentEntry.endpoints.length, 0);
    const visibleTestCount = filteredEnvironments.reduce(
        (count, environmentEntry) => count + environmentEntry.endpoints.reduce((endpointCount, endpointEntry) => endpointCount + endpointEntry.tests.length, 0),
        0
    );

    resultsSummary.textContent = resultsSearchTerm
        ? `${run.passedTests} passed, ${run.failedTests} failed overall | ${visibleEndpointCount} endpoints and ${visibleTestCount} tests shown for "${searchDisplayTerm}"`
        : `${run.passedTests} passed, ${run.failedTests} failed across ${run.environments.length} environments.`;
    updateResultButtons(true);
    synchronizeResultExpansionState(run);

    if (filteredEnvironments.length === 0) {
        environmentContainer.innerHTML = `<section class="empty-state"><h3>No matching results</h3><p>No APIs, endpoints, tests, assertions, or errors match "${escapeHtml(searchDisplayTerm)}".</p></section>`;
        return;
    }

    for (const environmentEntry of filteredEnvironments) {
        const { environment, environmentMatches, endpoints } = environmentEntry;
        const environmentKey = getResultEnvironmentKey(environment);
        const visibleTests = endpoints.flatMap((endpointEntry) => endpointEntry.tests);
        const visiblePassedTests = visibleTests.filter((test) => test.isSuccess).length;
        const visibleFailedTests = visibleTests.length - visiblePassedTests;

        const environmentNode = environmentTemplate.content.firstElementChild.cloneNode(true);
        environmentNode.open = resultsSearchTerm ? true : resultExpansionState.environments.get(environmentKey) ?? environment.failedTests > 0;
        environmentNode.addEventListener("toggle", () => {
            resultExpansionState.environments.set(environmentKey, environmentNode.open);
        });

        environmentNode.querySelector(".environment-name").innerHTML = highlightMatch(environment.name, resultsSearchTerm);
        environmentNode.querySelector(".environment-url").innerHTML = highlightMatch(environment.baseUrl, resultsSearchTerm);
        environmentNode.querySelector(".environment-stats").textContent = resultsSearchTerm && !environmentMatches
            ? `${visiblePassedTests} passed, ${visibleFailedTests} failed, ${visibleTests.length} matching tests`
            : `${environment.passedTests} passed, ${environment.failedTests} failed, ${environment.totalTests} total`;

        const environmentBadge = environmentNode.querySelector(".environment-badge");
        const environmentIsPassing = resultsSearchTerm ? visibleFailedTests === 0 : environment.failedTests === 0;
        environmentBadge.textContent = environmentIsPassing ? "Passing" : "Issues";
        environmentBadge.className = `environment-badge ${environmentIsPassing ? "passing" : "failing"}`;

        const endpointList = environmentNode.querySelector(".endpoint-list");

        for (const endpointEntry of endpoints) {
            const { endpoint, endpointMatches, tests } = endpointEntry;
            const endpointKey = getResultEndpointKey(environment, endpoint);
            const endpointNode = endpointTemplate.content.firstElementChild.cloneNode(true);
            endpointNode.open = resultsSearchTerm ? true : resultExpansionState.endpoints.get(endpointKey) ?? !endpoint.isSuccess;
            endpointNode.addEventListener("toggle", () => {
                resultExpansionState.endpoints.set(endpointKey, endpointNode.open);
            });

            endpointNode.querySelector(".endpoint-name").innerHTML = highlightMatch(endpoint.name, resultsSearchTerm);
            endpointNode.querySelector(".endpoint-meta").innerHTML =
                `${highlightMatch(endpoint.method, resultsSearchTerm)} ${highlightMatch(endpoint.requestUrl, resultsSearchTerm)} - ${Math.round(endpoint.durationMs)} ms`;

            const endpointBadge = endpointNode.querySelector(".endpoint-badge");
            const endpointVisibleFailedTests = tests.filter((test) => !test.isSuccess).length;
            const endpointIsPassing = resultsSearchTerm ? endpointVisibleFailedTests === 0 : endpoint.isSuccess;
            endpointBadge.textContent = endpointIsPassing ? "Pass" : "Fail";
            endpointBadge.className = `endpoint-badge ${endpointIsPassing ? "passing" : "failing"}`;

            endpointNode.querySelector(".response-body").textContent =
                endpoint.responseBody || endpoint.errorMessage || "(empty response)";

            const testList = endpointNode.querySelector(".test-list");

            tests.forEach((test, testIndex) => {
                const testKey = getResultTestKey(environment, endpoint, test, testIndex);
                const testNode = testTemplate.content.firstElementChild.cloneNode(true);
                testNode.open = resultsSearchTerm ? true : resultExpansionState.tests.get(testKey) ?? !test.isSuccess;
                testNode.addEventListener("toggle", () => {
                    resultExpansionState.tests.set(testKey, testNode.open);
                });

                testNode.querySelector(".test-name").innerHTML = highlightMatch(test.name, resultsSearchTerm);

                const testBadge = testNode.querySelector(".test-badge");
                testBadge.textContent = test.isSuccess ? "Pass" : "Fail";
                testBadge.className = `test-badge ${test.isSuccess ? "passing" : "failing"}`;

                const expectedText = `Expected ${test.expectedStatus}, actual ${test.actualStatus ?? "n/a"}`;
                const errorSuffix = test.errorMessage ? ` - ${test.errorMessage}` : "";
                testNode.querySelector(".test-status-line").innerHTML = highlightMatch(`${expectedText}${errorSuffix}`, resultsSearchTerm);

                const assertionList = testNode.querySelector(".assertion-list");
                if (test.assertions.length === 0) {
                    assertionList.innerHTML = "<li>No assertions configured.</li>";
                } else {
                    for (const assertion of test.assertions) {
                        const listItem = document.createElement("li");
                        listItem.className = assertion.isSuccess ? "assertion-pass" : "assertion-fail";
                        listItem.innerHTML = `${highlightMatch(assertion.rule, resultsSearchTerm)} on ${highlightMatch(assertion.field, resultsSearchTerm)}: ${highlightMatch(assertion.message, resultsSearchTerm)}`;
                        assertionList.appendChild(listItem);
                    }
                }

                testList.appendChild(testNode);
            });

            endpointList.appendChild(endpointNode);
        }

        environmentContainer.appendChild(environmentNode);
    }
}

function filterRunEnvironments(run, searchTerm) {
    if (!searchTerm) {
        return run.environments.map((environment) => ({
            environment,
            environmentMatches: false,
            endpoints: environment.endpoints.map((endpoint) => ({
                endpoint,
                endpointMatches: false,
                tests: endpoint.tests
            }))
        }));
    }

    return run.environments
        .map((environment) => {
            const environmentMatches =
                matchesSearch(environment.name, searchTerm) ||
                matchesSearch(environment.baseUrl, searchTerm);

            const endpoints = environment.endpoints
                .map((endpoint) => {
                    const endpointMatches = environmentMatches || endpointMatchesRunSearch(endpoint, searchTerm);
                    return {
                        endpoint,
                        endpointMatches,
                        tests: endpointMatches
                            ? endpoint.tests
                            : endpoint.tests.filter((test) => testMatchesRunSearch(test, searchTerm))
                    };
                })
                .filter((endpointEntry) => endpointEntry.endpointMatches || endpointEntry.tests.length > 0);

            if (!environmentMatches && endpoints.length === 0) {
                return null;
            }

            return {
                environment,
                environmentMatches,
                endpoints
            };
        })
        .filter((entry) => entry !== null);
}

function endpointMatchesRunSearch(endpoint, searchTerm) {
    return (
        matchesSearch(endpoint.name, searchTerm) ||
        matchesSearch(endpoint.method, searchTerm) ||
        matchesSearch(endpoint.requestUrl, searchTerm) ||
        matchesSearch(endpoint.errorMessage, searchTerm)
    );
}

function testMatchesRunSearch(test, searchTerm) {
    return (
        matchesSearch(test.name, searchTerm) ||
        matchesSearch(test.errorMessage, searchTerm) ||
        test.assertions.some((assertion) => assertionMatchesSearch(assertion, searchTerm))
    );
}

function assertionMatchesSearch(assertion, searchTerm) {
    return (
        matchesSearch(assertion.field, searchTerm) ||
        matchesSearch(assertion.rule, searchTerm) ||
        matchesSearch(assertion.message, searchTerm)
    );
}

function synchronizeResultExpansionState(run) {
    const environmentKeys = new Set();
    const endpointKeys = new Set();
    const testKeys = new Set();

    for (const environment of run.environments) {
        const environmentKey = getResultEnvironmentKey(environment);
        environmentKeys.add(environmentKey);

        if (!resultExpansionState.environments.has(environmentKey)) {
            resultExpansionState.environments.set(environmentKey, environment.failedTests > 0);
        }

        for (const endpoint of environment.endpoints) {
            const endpointKey = getResultEndpointKey(environment, endpoint);
            endpointKeys.add(endpointKey);

            if (!resultExpansionState.endpoints.has(endpointKey)) {
                resultExpansionState.endpoints.set(endpointKey, !endpoint.isSuccess);
            }

            endpoint.tests.forEach((test, testIndex) => {
                const testKey = getResultTestKey(environment, endpoint, test, testIndex);
                testKeys.add(testKey);

                if (!resultExpansionState.tests.has(testKey)) {
                    resultExpansionState.tests.set(testKey, !test.isSuccess);
                }
            });
        }
    }

    pruneState(resultExpansionState.environments, environmentKeys);
    pruneState(resultExpansionState.endpoints, endpointKeys);
    pruneState(resultExpansionState.tests, testKeys);
}

function pruneState(stateMap, validKeys) {
    for (const key of stateMap.keys()) {
        if (!validKeys.has(key)) {
            stateMap.delete(key);
        }
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
    setStatusError(true);
    document.getElementById("runStatus").textContent = error.message || "An unexpected dashboard error occurred.";
}

function formatDate(value) {
    if (!value) {
        return "-";
    }

    return new Date(value).toLocaleString();
}

async function buildErrorMessage(response, fallbackMessage) {
    try {
        const payload = await response.json();
        if (payload && typeof payload.error === "string" && payload.error.trim()) {
            return payload.error;
        }

        if (payload && typeof payload.title === "string" && payload.title.trim()) {
            return payload.title;
        }
    } catch {
        // Fall back to a generic message below.
    }

    return `${fallbackMessage} with status ${response.status}`;
}

function setBusy(isBusy) {
    runButton.disabled = isBusy;
    refreshButton.disabled = isBusy;
    runButton.innerHTML = isBusy
        ? "<i class=\"fa-solid fa-spinner fa-spin button-icon\"></i>Running..."
        : "<i class=\"fa-solid fa-play button-icon\"></i>Run Tests";
    updateSelectionButtons(Boolean(suiteManifest) && !isBusy);
    updateResultButtons(Boolean(lastRunState?.lastRun) && !isBusy);
}

function updateSelectionButtons(isEnabled) {
    selectAllButton.disabled = !isEnabled;
    clearAllButton.disabled = !isEnabled;
    expandSelectionButton.disabled = !isEnabled;
    collapseSelectionButton.disabled = !isEnabled;
}

function updateResultButtons(isEnabled) {
    expandResultsButton.disabled = !isEnabled;
    collapseResultsButton.disabled = !isEnabled;
}

function setStatusError(hasError) {
    document.getElementById("runStatus").classList.toggle("status-error", hasError);
}

function getResultEnvironmentKey(environment) {
    return `${environment.name}|${environment.baseUrl}`;
}

function getResultEndpointKey(environment, endpoint) {
    return `${getResultEnvironmentKey(environment)}|${endpoint.method}|${endpoint.requestUrl}|${endpoint.name}`;
}

function getResultTestKey(environment, endpoint, test, testIndex) {
    return `${getResultEndpointKey(environment, endpoint)}|${test.name}|${testIndex}`;
}

function setSelectionExpansion(isOpen) {
    if (!suiteManifest) {
        return;
    }

    for (const environment of suiteManifest.environments) {
        selectionExpansionState.environments.set(environment.id, isOpen);

        for (const endpoint of environment.endpoints) {
            selectionExpansionState.endpoints.set(endpoint.id, isOpen);
        }
    }

    renderSelection(suiteManifest);
}

function setResultExpansion(isOpen) {
    const run = lastRunState?.lastRun;
    if (!run) {
        return;
    }

    for (const environment of run.environments) {
        resultExpansionState.environments.set(getResultEnvironmentKey(environment), isOpen);

        for (const endpoint of environment.endpoints) {
            resultExpansionState.endpoints.set(getResultEndpointKey(environment, endpoint), isOpen);

            endpoint.tests.forEach((test, testIndex) => {
                resultExpansionState.tests.set(getResultTestKey(environment, endpoint, test, testIndex), isOpen);
            });
        }
    }

    renderState(lastRunState);
}

function matchesSearch(value, searchTerm) {
    return typeof value === "string" && value.toLowerCase().includes(searchTerm);
}

function highlightMatch(value, searchTerm) {
    const safeValue = escapeHtml(value);
    if (!searchTerm || typeof value !== "string") {
        return safeValue;
    }

    const pattern = new RegExp(`(${escapeRegExp(searchTerm)})`, "ig");
    return safeValue.replace(pattern, "<mark class=\"search-highlight\">$1</mark>");
}

function escapeRegExp(value) {
    return String(value).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function escapeHtml(value) {
    return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
}

runButton.addEventListener("click", runSuite);
refreshButton.addEventListener("click", async () => {
    try {
        renderState(await fetchState());
    } catch (error) {
        renderError(error);
    }
});

selectAllButton.addEventListener("click", () => {
    if (!suiteManifest) {
        return;
    }

    selectedTestIds = new Set(getAllTestIds(suiteManifest));
    renderSelection(suiteManifest);
});

clearAllButton.addEventListener("click", () => {
    selectedTestIds = new Set();
    renderSelection(suiteManifest);
});

expandSelectionButton.addEventListener("click", () => setSelectionExpansion(true));
collapseSelectionButton.addEventListener("click", () => setSelectionExpansion(false));
expandResultsButton.addEventListener("click", () => setResultExpansion(true));
collapseResultsButton.addEventListener("click", () => setResultExpansion(false));
selectionSearchInput.addEventListener("input", () => {
    selectionSearchTerm = selectionSearchInput.value.trim().toLowerCase();

    if (suiteManifest) {
        renderSelection(suiteManifest);
    }
});
resultsSearchInput.addEventListener("input", () => {
    resultsSearchTerm = resultsSearchInput.value.trim().toLowerCase();

    if (lastRunState) {
        renderState(lastRunState);
    }
});

initializeDashboard();
