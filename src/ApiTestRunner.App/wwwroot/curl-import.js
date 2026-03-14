const analyzeButton = document.getElementById("analyzeButton");
const analyzeStatus = document.getElementById("analyzeStatus");
const parseResponseButton = document.getElementById("parseResponseButton");
const responseStatus = document.getElementById("responseStatus");
const addAssertionButton = document.getElementById("addAssertionButton");
const curlInput = document.getElementById("curlInput");
const responseBodyInput = document.getElementById("responseBodyInput");
const assertionFieldSelect = document.getElementById("assertionFieldSelect");
const assertionRuleSelect = document.getElementById("assertionRuleSelect");
const assertionValueContainer = document.getElementById("assertionValueContainer");
const assertionList = document.getElementById("assertionList");
const analysisContainer = document.getElementById("analysisContainer");

const assertionRuleDefinitions = {
    equals: { label: "equals", valueMode: "typed" },
    notEquals: { label: "notEquals", valueMode: "typed" },
    type: {
        label: "type",
        valueMode: "select",
        options: ["string", "number", "boolean", "object", "array"]
    },
    containsText: { label: "containsText", valueMode: "text" },
    startsWith: { label: "startsWith", valueMode: "text" },
    endsWith: { label: "endsWith", valueMode: "text" },
    notEmpty: {
        label: "notEmpty",
        valueMode: "select",
        options: [
            { label: "true", value: true },
            { label: "false", value: false }
        ]
    },
    minCount: { label: "minCount", valueMode: "number" },
    maxCount: { label: "maxCount", valueMode: "number" },
    count: { label: "count", valueMode: "number" }
};

let parsedResponseFields = [];
let parsedResponseObject = null;
let assertionDrafts = [];

async function analyzeCurlCommand() {
    const command = curlInput.value.trim();
    if (!command) {
        renderStatus("Paste a cURL command first.", true);
        return;
    }

    setBusy(true);

    try {
        const response = await fetch("/api/tools/curl/analyze", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                command,
                responseBody: responseBodyInput.value.trim() || null,
                assertions: assertionDrafts.map((draft) => ({
                    field: draft.field,
                    rule: draft.rule,
                    value: draft.value
                }))
            })
        });

        if (!response.ok) {
            throw new Error(await buildErrorMessage(response, "Analyze request failed"));
        }

        const result = await response.json();
        renderResult(result);
        renderStatus(
            result.warnings && result.warnings.length > 0
                ? "Analysis completed with warnings."
                : "Analysis completed.",
            false);
    } catch (error) {
        analysisContainer.innerHTML = "";
        renderStatus(error.message || "Unable to analyze the provided cURL command.", true);
    } finally {
        setBusy(false);
    }
}

function parseResponseBody() {
    const responseBody = responseBodyInput.value.trim();
    if (!responseBody) {
        parsedResponseFields = [];
        parsedResponseObject = null;
        assertionDrafts = [];
        renderAssertionBuilder();
        renderResponseStatus("No response body parsed yet.", false);
        return;
    }

    try {
        parsedResponseObject = JSON.parse(responseBody);
        parsedResponseFields = collectResponseFields(parsedResponseObject);
        assertionDrafts = [];
        renderAssertionBuilder();
        renderResponseStatus(`${parsedResponseFields.length} selectable fields detected.`, false);
    } catch (error) {
        parsedResponseFields = [];
        parsedResponseObject = null;
        assertionDrafts = [];
        renderAssertionBuilder();
        renderResponseStatus(error.message || "Response body is not valid JSON.", true);
    }
}

function collectResponseFields(value, path = "") {
    const fields = [];
    const type = getJsonValueType(value);

    if (path) {
        fields.push({
            path,
            type,
            sample: value
        });
    }

    if (Array.isArray(value)) {
        value.forEach((item, index) => {
            fields.push(...collectResponseFields(item, `${path}[${index}]`));
        });

        return fields;
    }

    if (value && typeof value === "object") {
        Object.entries(value).forEach(([key, childValue]) => {
            const childPath = path ? `${path}.${key}` : key;
            fields.push(...collectResponseFields(childValue, childPath));
        });
    }

    return fields;
}

function renderAssertionBuilder() {
    renderFieldOptions();
    renderRuleOptions();
    renderValueInput();
    renderAssertionDrafts();
    addAssertionButton.disabled = parsedResponseFields.length === 0;
}

function renderFieldOptions() {
    assertionFieldSelect.innerHTML = "";

    if (parsedResponseFields.length === 0) {
        const option = document.createElement("option");
        option.textContent = "Parse a response body first";
        option.value = "";
        assertionFieldSelect.appendChild(option);
        assertionFieldSelect.disabled = true;
        return;
    }

    assertionFieldSelect.disabled = false;

    for (const field of parsedResponseFields) {
        const option = document.createElement("option");
        option.value = field.path;
        option.textContent = `${field.path} (${field.type})`;
        assertionFieldSelect.appendChild(option);
    }
}

function renderRuleOptions() {
    assertionRuleSelect.innerHTML = "";

    const field = getSelectedField();
    if (!field) {
        assertionRuleSelect.disabled = true;
        return;
    }

    const supportedRules = getRulesForFieldType(field.type);
    supportedRules.forEach((rule) => {
        const option = document.createElement("option");
        option.value = rule;
        option.textContent = assertionRuleDefinitions[rule].label;
        assertionRuleSelect.appendChild(option);
    });

    assertionRuleSelect.disabled = false;
}

function renderValueInput() {
    assertionValueContainer.innerHTML = "";
    const field = getSelectedField();
    const rule = assertionRuleSelect.value;

    const label = document.createElement("span");
    label.textContent = "Value";
    assertionValueContainer.appendChild(label);

    if (!field || !rule) {
        const input = document.createElement("input");
        input.className = "tool-input-inline";
        input.type = "text";
        input.disabled = true;
        assertionValueContainer.appendChild(input);
        return;
    }

    const definition = assertionRuleDefinitions[rule];

    if (definition.valueMode === "select") {
        const select = document.createElement("select");
        select.id = "assertionValueInput";
        select.className = "tool-select";

        definition.options.forEach((optionDefinition) => {
            const option = document.createElement("option");
            if (typeof optionDefinition === "string") {
                option.value = optionDefinition;
                option.textContent = optionDefinition;
            } else {
                option.value = String(optionDefinition.value);
                option.textContent = optionDefinition.label;
            }

            select.appendChild(option);
        });

        assertionValueContainer.appendChild(select);
        return;
    }

    const input = document.createElement("input");
    input.id = "assertionValueInput";
    input.className = "tool-input-inline";

    if (definition.valueMode === "number") {
        input.type = "number";
        input.step = "1";
        input.value = Array.isArray(field.sample) ? String(field.sample.length) : "1";
    } else {
        input.type = "text";
        input.value = definition.valueMode === "typed" ? formatSample(field.sample) : "";
    }

    assertionValueContainer.appendChild(input);
}

function getSelectedField() {
    if (parsedResponseFields.length === 0) {
        return null;
    }

    return parsedResponseFields.find((field) => field.path === assertionFieldSelect.value) ?? parsedResponseFields[0];
}

function getRulesForFieldType(fieldType) {
    const commonRules = ["equals", "notEquals", "type", "notEmpty"];

    switch (fieldType) {
        case "string":
            return [...commonRules, "containsText", "startsWith", "endsWith"];
        case "array":
            return [...commonRules, "minCount", "maxCount", "count"];
        case "object":
            return ["type", "notEmpty"];
        case "number":
        case "boolean":
            return ["equals", "notEquals", "type"];
        default:
            return commonRules;
    }
}

function addAssertionDraft() {
    const field = getSelectedField();
    const rule = assertionRuleSelect.value;

    if (!field || !rule) {
        renderResponseStatus("Parse a response body and choose a field first.", true);
        return;
    }

    const valueInput = document.getElementById("assertionValueInput");
    const value = convertAssertionValue(rule, field.type, valueInput);

    assertionDrafts.push({
        field: field.path,
        rule,
        value
    });

    renderAssertionDrafts();
}

function convertAssertionValue(rule, fieldType, input) {
    const definition = assertionRuleDefinitions[rule];

    if (definition.valueMode === "select") {
        if (rule === "notEmpty") {
            return input.value === "true";
        }

        return input.value;
    }

    if (definition.valueMode === "number") {
        return Number.parseInt(input.value, 10);
    }

    if (definition.valueMode === "text") {
        return input.value;
    }

    const text = input.value;

    switch (fieldType) {
        case "number":
            return Number(text);
        case "boolean":
            return text.toLowerCase() === "true";
        case "object":
        case "array":
            try {
                return JSON.parse(text);
            } catch {
                return text;
            }
        default:
            return text;
    }
}

function renderAssertionDrafts() {
    assertionList.innerHTML = "";

    if (assertionDrafts.length === 0) {
        assertionList.innerHTML = "<p class=\"result-note\">No assertion rules added yet.</p>";
        return;
    }

    assertionDrafts.forEach((draft, index) => {
        const item = document.createElement("div");
        item.className = "assertion-draft-item";

        const text = document.createElement("span");
        text.textContent = `${draft.field} -> ${draft.rule}: ${formatSample(draft.value)}`;

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "ghost-button inline-button";
        removeButton.textContent = "Remove";
        removeButton.addEventListener("click", () => {
            assertionDrafts.splice(index, 1);
            renderAssertionDrafts();
        });

        item.appendChild(text);
        item.appendChild(removeButton);
        assertionList.appendChild(item);
    });
}

function renderResult(result) {
    analysisContainer.innerHTML = "";

    if (result.warnings && result.warnings.length > 0) {
        analysisContainer.appendChild(renderWarningCard(result.warnings));
    }

    analysisContainer.appendChild(renderRequestCard(result.request));
    analysisContainer.appendChild(renderEnvironmentCard(result.environment));
    analysisContainer.appendChild(renderEndpointCard(result.endpoint));
}

function renderWarningCard(warnings) {
    const card = document.createElement("section");
    card.className = "preview-card warning-card";
    card.innerHTML = "<h2>Warnings</h2><p class=\"result-note\">The analyzer continued with generated suggestions even though the configured YAML suite could not be loaded fully.</p>";

    const list = document.createElement("ul");
    list.className = "warning-list";

    warnings.forEach((warning) => {
        const item = document.createElement("li");
        item.textContent = warning;
        list.appendChild(item);
    });

    card.appendChild(list);
    return card;
}

function renderRequestCard(request) {
    const card = createCard("Parsed request", "What the app extracted from the cURL command.");
    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Method", request.method));
    details.appendChild(createDetail("Base URL", request.baseUrl));
    details.appendChild(createDetail("Path", request.path));
    details.appendChild(createDetail("Relative path", request.relativePath || request.path));
    details.appendChild(createDetail("URL", request.url));
    details.appendChild(createDetail("Query", request.query && Object.keys(request.query).length > 0 ? JSON.stringify(request.query, null, 2) : "(none)"));
    details.appendChild(createDetail("Headers", request.headers && Object.keys(request.headers).length > 0 ? JSON.stringify(request.headers, null, 2) : "(none)"));
    details.appendChild(createDetail("Body", formatSample(request.body) || request.rawBody || "(none)"));
    card.appendChild(details);
    return card;
}

function renderEnvironmentCard(environment) {
    const card = createCard("Environment scan", "Checks all loaded environment YAML definitions for an existing URL match before suggesting a new environment file.");
    card.appendChild(createBadgeRow(environment.exists, environment.exists ? "Environment found" : "Environment missing"));

    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Matched environments", environment.matchedEnvironmentNames.length > 0 ? environment.matchedEnvironmentNames.join(", ") : "(none)"));
    details.appendChild(createDetail("Suggested environment name", environment.suggestedName));

    if (environment.suggestedFilePath) {
        details.appendChild(createDetail("Suggested file path", environment.suggestedFilePath));
    }

    card.appendChild(details);

    if (environment.suggestedYaml) {
        card.appendChild(createCopyAction(environment.suggestedYaml, "Copy environment YAML"));
        const preview = document.createElement("pre");
        preview.className = "code-block";
        preview.textContent = environment.suggestedYaml;
        card.appendChild(preview);
    }

    return card;
}

function renderEndpointCard(endpoint) {
    const card = createCard("Endpoint scan", "Checks whether the endpoint already exists, then generates endpoint YAML including any assertion rules you added.");
    card.appendChild(createBadgeRow(endpoint.exists, endpoint.exists ? "Endpoint found" : "Endpoint missing"));

    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Matched environments", endpoint.matchedEnvironmentNames.length > 0 ? endpoint.matchedEnvironmentNames.join(", ") : "(none)"));
    details.appendChild(createDetail("Suggested endpoint name", endpoint.suggestedName));

    if (endpoint.suggestedFilePath) {
        details.appendChild(createDetail("Suggested file path", endpoint.suggestedFilePath));
    }

    card.appendChild(details);

    if (endpoint.suggestedYaml) {
        card.appendChild(createCopyAction(endpoint.suggestedYaml, "Copy endpoint YAML"));
        const preview = document.createElement("pre");
        preview.className = "code-block";
        preview.textContent = endpoint.suggestedYaml;
        card.appendChild(preview);
    }

    return card;
}

function createCard(title, summary) {
    const card = document.createElement("section");
    card.className = "preview-card";
    card.innerHTML = `<h2>${escapeHtml(title)}</h2><p class="result-note">${escapeHtml(summary)}</p>`;
    return card;
}

function createBadgeRow(isPassing, text) {
    const wrapper = document.createElement("div");
    wrapper.className = "badge-row";

    const badge = document.createElement("span");
    badge.className = `status-badge ${isPassing ? "passing" : "failing"}`;
    badge.textContent = text;

    wrapper.appendChild(badge);
    return wrapper;
}

function createDetail(term, description) {
    const wrapper = document.createElement("div");

    const dt = document.createElement("dt");
    dt.textContent = term;

    const dd = document.createElement("dd");
    dd.textContent = description;

    wrapper.appendChild(dt);
    wrapper.appendChild(dd);
    return wrapper;
}

function createCopyAction(text, label) {
    const actionRow = document.createElement("div");
    actionRow.className = "copy-action-row";

    const button = document.createElement("button");
    button.type = "button";
    button.className = "ghost-button inline-button copy-button";
    button.innerHTML = "&#128203; " + escapeHtml(label);
    button.addEventListener("click", async () => {
        const originalLabel = button.innerHTML;

        try {
            await navigator.clipboard.writeText(text);
            button.innerHTML = "&#10003; Copied";
        } catch {
            button.innerHTML = "Copy failed";
        }

        window.setTimeout(() => {
            button.innerHTML = originalLabel;
        }, 1400);
    });

    actionRow.appendChild(button);
    return actionRow;
}

function formatSample(value) {
    if (value === null || typeof value === "undefined") {
        return "";
    }

    if (typeof value === "string") {
        return value;
    }

    return JSON.stringify(value, null, 2);
}

function getJsonValueType(value) {
    if (Array.isArray(value)) {
        return "array";
    }

    if (value === null) {
        return "null";
    }

    switch (typeof value) {
        case "string":
            return "string";
        case "number":
            return "number";
        case "boolean":
            return "boolean";
        case "object":
            return "object";
        default:
            return "unknown";
    }
}

function renderStatus(message, isError) {
    analyzeStatus.textContent = message;
    analyzeStatus.classList.toggle("status-error", Boolean(isError));
}

function renderResponseStatus(message, isError) {
    responseStatus.textContent = message;
    responseStatus.classList.toggle("status-error", Boolean(isError));
}

function setBusy(isBusy) {
    analyzeButton.disabled = isBusy;
    parseResponseButton.disabled = isBusy;
    addAssertionButton.disabled = isBusy || parsedResponseFields.length === 0;
    analyzeButton.textContent = isBusy ? "Analyzing..." : "Analyze Command";
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
        // Fall back below.
    }

    return `${fallbackMessage} with status ${response.status}`;
}

function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
}

parseResponseButton.addEventListener("click", parseResponseBody);
assertionFieldSelect.addEventListener("change", () => {
    renderRuleOptions();
    renderValueInput();
});
assertionRuleSelect.addEventListener("change", renderValueInput);
addAssertionButton.addEventListener("click", addAssertionDraft);
analyzeButton.addEventListener("click", analyzeCurlCommand);

renderAssertionBuilder();
