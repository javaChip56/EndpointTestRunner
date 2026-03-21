const analyzeButton = document.getElementById("analyzeButton");
const analyzeStatus = document.getElementById("analyzeStatus");
const responseStatus = document.getElementById("responseStatus");
const addAssertionButton = document.getElementById("addAssertionButton");
const addTestButton = document.getElementById("addTestButton");
const curlInput = document.getElementById("curlInput");
const responseBodyInput = document.getElementById("responseBodyInput");
const formatResponseButton = document.getElementById("formatResponseButton");
const toggleResponseWrapButton = document.getElementById("toggleResponseWrapButton");
const testNameInput = document.getElementById("testNameInput");
const expectedStatusInput = document.getElementById("expectedStatusInput");
const testDraftList = document.getElementById("testDraftList");
const assertionBuilderGrid = document.getElementById("assertionBuilderGrid");
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
    contains: { label: "contains", valueMode: "typed" },
    notEmpty: {
        label: "notEmpty",
        valueMode: "select",
        options: [
            { label: "true", value: true },
            { label: "false", value: false }
        ]
    },
    greaterThan: { label: "greaterThan", valueMode: "number" },
    greaterThanOrEqual: { label: "greaterThanOrEqual", valueMode: "number" },
    lessThan: { label: "lessThan", valueMode: "number" },
    lessThanOrEqual: { label: "lessThanOrEqual", valueMode: "number" },
    minCount: { label: "minCount", valueMode: "number" },
    maxCount: { label: "maxCount", valueMode: "number" },
    count: { label: "count", valueMode: "number" }
};

let parsedResponseFields = [];
let parsedResponseObject = null;
let testDrafts = [];
let currentTestDraftId = null;
let nextTestDraftNumber = 1;
let lastParsedResponseBody = "";
let isResponseWrapped = true;

async function analyzeCurlCommand() {
    const command = curlInput.value.trim();
    if (!command) {
        renderStatus("Paste a cURL command first.", true);
        return;
    }

    parseResponseBody();
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
                tests: buildAnalyzePayloadTests()
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

function buildAnalyzePayloadTests() {
    return testDrafts.map((draft, index) => ({
        name: draft.name.trim() || `Test ${index + 1}`,
        expectedStatus: normalizeExpectedStatus(draft.expectedStatus),
        assertions: draft.assertions.map((assertion) => ({
            field: assertion.field,
            rule: assertion.rule,
            value: assertion.value
        }))
    }));
}

function parseResponseBody() {
    const responseBody = responseBodyInput.value.trim();
    if (!responseBody) {
        parsedResponseFields = [];
        parsedResponseObject = null;
        lastParsedResponseBody = "";
        renderAssertionBuilder();
        renderResponseStatus("No response body parsed yet.", false);
        return;
    }

    if (responseBody === lastParsedResponseBody && parsedResponseFields.length > 0) {
        renderResponseStatus(`${parsedResponseFields.length} selectable fields detected.`, false);
        return;
    }

    try {
        parsedResponseObject = JSON.parse(responseBody);
        parsedResponseFields = collectResponseFields(parsedResponseObject);
        testDrafts = testDrafts.map((draft) => ({
            ...draft,
            assertions: draft.assertions.filter((assertion) =>
                parsedResponseFields.some((field) => field.path === assertion.field))
        }));
        lastParsedResponseBody = responseBody;
        renderAssertionBuilder();
        renderResponseStatus(`${parsedResponseFields.length} selectable fields detected.`, false);
    } catch (error) {
        parsedResponseFields = [];
        parsedResponseObject = null;
        lastParsedResponseBody = "";
        renderAssertionBuilder();
        renderResponseStatus(error.message || "Response body is not valid JSON.", true);
    }
}

function formatResponseBody() {
    const responseBody = responseBodyInput.value.trim();
    if (!responseBody) {
        renderResponseStatus("Paste a response body first.", true);
        return;
    }

    try {
        const parsed = JSON.parse(responseBody);
        responseBodyInput.value = JSON.stringify(parsed, null, 2);
        parseResponseBody();
        renderResponseStatus("Response body formatted.", false);
    } catch (error) {
        renderResponseStatus(error.message || "Response body is not valid JSON.", true);
    }
}

function toggleResponseWrap() {
    isResponseWrapped = !isResponseWrapped;
    responseBodyInput.wrap = isResponseWrapped ? "soft" : "off";
    responseBodyInput.classList.toggle("is-wrapped", isResponseWrapped);
    responseBodyInput.classList.toggle("is-unwrapped", !isResponseWrapped);
    toggleResponseWrapButton.innerHTML = isResponseWrapped
        ? "<i class=\"fa-solid fa-text-width button-icon\"></i>Disable Wrap"
        : "<i class=\"fa-solid fa-align-left button-icon\"></i>Enable Wrap";
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
    ensureAtLeastOneTestDraft();
    renderTestDraftList();
    syncCurrentTestInputs();
    renderFieldOptions();
    renderRuleOptions();
    renderValueInput();
    renderAssertionDrafts();
    addAssertionButton.disabled = parsedResponseFields.length === 0 || !getCurrentTestDraft();
}

function ensureAtLeastOneTestDraft() {
    if (testDrafts.length === 0) {
        const initialDraft = createTestDraft();
        testDrafts.push(initialDraft);
        currentTestDraftId = initialDraft.id;
        return;
    }

    if (!testDrafts.some((draft) => draft.id === currentTestDraftId)) {
        currentTestDraftId = testDrafts[0].id;
    }
}

function createTestDraft() {
    const testNumber = nextTestDraftNumber++;
    return {
        id: `test-${Date.now()}-${testNumber}`,
        name: `Test ${testNumber}`,
        expectedStatus: 200,
        assertions: []
    };
}

function getCurrentTestDraft() {
    return testDrafts.find((draft) => draft.id === currentTestDraftId) ?? null;
}

function renderTestDraftList() {
    testDraftList.innerHTML = "";

    if (testDrafts.length === 0) {
        testDraftList.innerHTML = "<p class=\"result-note\">No tests drafted yet.</p>";
        return;
    }

    testDrafts.forEach((draft, index) => {
        const item = document.createElement("div");
        item.className = "test-draft-item";

        const info = document.createElement("div");
        info.className = "test-draft-info";

        const title = document.createElement("strong");
        title.textContent = draft.name.trim() || `Test ${index + 1}`;

        const meta = document.createElement("span");
        meta.className = "test-draft-meta";
        meta.textContent = `Expected ${normalizeExpectedStatus(draft.expectedStatus)} | ${draft.assertions.length} assertions`;

        info.appendChild(title);
        info.appendChild(meta);

        const actions = document.createElement("div");
        actions.className = "test-draft-actions";

        const editButton = document.createElement("button");
        editButton.type = "button";
        editButton.className = `ghost-button inline-button${draft.id === currentTestDraftId ? " is-active" : ""}`;
        editButton.textContent = draft.id === currentTestDraftId ? "Editing" : "Edit";
        editButton.addEventListener("click", () => {
            currentTestDraftId = draft.id;
            renderAssertionBuilder();
        });

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "ghost-button inline-button";
        removeButton.textContent = "Remove";
        removeButton.addEventListener("click", () => removeTestDraft(draft.id));

        actions.appendChild(editButton);
        actions.appendChild(removeButton);

        item.appendChild(info);
        item.appendChild(actions);
        testDraftList.appendChild(item);
    });
}

function removeTestDraft(testId) {
    testDrafts = testDrafts.filter((draft) => draft.id !== testId);

    if (testDrafts.length === 0) {
        const replacementDraft = createTestDraft();
        testDrafts = [replacementDraft];
        currentTestDraftId = replacementDraft.id;
    } else if (currentTestDraftId === testId) {
        currentTestDraftId = testDrafts[0].id;
    }

    renderAssertionBuilder();
}

function syncCurrentTestInputs() {
    const currentDraft = getCurrentTestDraft();
    if (!currentDraft) {
        testNameInput.value = "";
        expectedStatusInput.value = "200";
        return;
    }

    testNameInput.value = currentDraft.name;
    expectedStatusInput.value = String(normalizeExpectedStatus(currentDraft.expectedStatus));
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
    const previousContainsField = document.getElementById("assertionContainsFieldInput")?.value ?? "";
    const previousContainsRule = document.getElementById("assertionContainsRuleInput")?.value ?? "";
    const previousContainsValue = document.getElementById("assertionContainsValueInput")?.value ?? "";
    assertionValueContainer.innerHTML = "";
    const field = getSelectedField();
    const rule = assertionRuleSelect.value;

    const label = document.createElement("span");
    label.textContent = "Value";
    assertionValueContainer.appendChild(label);
    const isContainsRule = rule === "contains";
    assertionBuilderGrid.classList.toggle("has-complex-value", isContainsRule);
    assertionValueContainer.classList.toggle("field-stack-wide", isContainsRule);

    if (!field || !rule) {
        assertionBuilderGrid.classList.remove("has-complex-value");
        assertionValueContainer.classList.remove("field-stack-wide");
        const input = document.createElement("input");
        input.className = "tool-input-inline";
        input.type = "text";
        input.disabled = true;
        assertionValueContainer.appendChild(input);
        return;
    }

    const definition = assertionRuleDefinitions[rule];

    if (rule === "contains") {
        renderContainsValueInput(field, previousContainsField, previousContainsRule, previousContainsValue);
        return;
    }

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
        input.step = "any";
        input.value = Array.isArray(field.sample) ? String(field.sample.length) : "1";
    } else {
        input.type = "text";
        input.value = definition.valueMode === "typed" ? formatSample(field.sample) : "";
    }

    assertionValueContainer.appendChild(input);
}

function renderContainsValueInput(field, selectedRelativeField, selectedRelativeRule, selectedRelativeValue) {
    const containsFieldOptions = getContainsFieldOptions(field?.sample);

    if (containsFieldOptions.length === 0) {
        const helper = document.createElement("span");
        helper.className = "helper-text";
        helper.textContent = "contains currently supports arrays of objects.";
        assertionValueContainer.appendChild(helper);

        const input = document.createElement("input");
        input.className = "tool-input-inline";
        input.type = "text";
        input.disabled = true;
        assertionValueContainer.appendChild(input);
        return;
    }

    const layout = document.createElement("div");
    layout.className = "contains-editor-grid";

    const fieldStack = document.createElement("label");
    fieldStack.className = "field-stack";

    const fieldLabel = document.createElement("span");
    fieldLabel.textContent = "Match field";
    fieldStack.appendChild(fieldLabel);

    const select = document.createElement("select");
    select.id = "assertionContainsFieldInput";
    select.className = "tool-select";

    containsFieldOptions.forEach((optionDefinition) => {
        const option = document.createElement("option");
        option.value = optionDefinition.path;
        option.textContent = `${optionDefinition.path} (${optionDefinition.type})`;
        select.appendChild(option);
    });

    if (selectedRelativeField && containsFieldOptions.some((option) => option.path === selectedRelativeField)) {
        select.value = selectedRelativeField;
    }

    select.addEventListener("change", () => {
        renderValueInput();
    });

    fieldStack.appendChild(select);
    layout.appendChild(fieldStack);

    const selectedFieldDefinition = containsFieldOptions.find((option) => option.path === select.value) ?? containsFieldOptions[0];
    const ruleStack = document.createElement("label");
    ruleStack.className = "field-stack";

    const ruleLabel = document.createElement("span");
    ruleLabel.textContent = "Match rule";
    ruleStack.appendChild(ruleLabel);

    const ruleSelect = document.createElement("select");
    ruleSelect.id = "assertionContainsRuleInput";
    ruleSelect.className = "tool-select";

    getContainsRulesForFieldType(selectedFieldDefinition.type).forEach((rule) => {
        const option = document.createElement("option");
        option.value = rule;
        option.textContent = assertionRuleDefinitions[rule].label;
        ruleSelect.appendChild(option);
    });

    if (selectedRelativeRule &&
        getContainsRulesForFieldType(selectedFieldDefinition.type).includes(selectedRelativeRule))
    {
        ruleSelect.value = selectedRelativeRule;
    }

    ruleSelect.addEventListener("change", () => {
        renderValueInput();
    });

    ruleStack.appendChild(ruleSelect);
    layout.appendChild(ruleStack);

    const valueStack = document.createElement("label");
    valueStack.className = "field-stack";

    const valueLabel = document.createElement("span");
    valueLabel.textContent = "Match value";
    valueStack.appendChild(valueLabel);

    const valueInput = createContainsValueInput(selectedFieldDefinition, ruleSelect.value, selectedRelativeValue);
    valueStack.appendChild(valueInput);
    layout.appendChild(valueStack);

    assertionValueContainer.appendChild(layout);
}

function createContainsValueInput(fieldDefinition, rule, previousValue) {
    const definition = assertionRuleDefinitions[rule];

    if (definition.valueMode === "select") {
        const select = document.createElement("select");
        select.id = "assertionContainsValueInput";
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

        if (previousValue) {
            select.value = previousValue;
        } else if (rule === "notEmpty") {
            select.value = "true";
        } else if (rule === "type") {
            select.value = fieldDefinition.type;
        }

        return select;
    }

    const input = document.createElement("input");
    input.id = "assertionContainsValueInput";
    input.className = "tool-input-inline";

    if (definition.valueMode === "number") {
        input.type = "number";
        input.step = "any";
    } else {
        input.type = "text";
    }

    input.value = previousValue || formatSample(fieldDefinition.sample);
    return input;
}

function getContainsFieldOptions(sample) {
    if (!Array.isArray(sample) || sample.length === 0) {
        return [];
    }

    const fieldMap = new Map();

    sample.forEach((item) => {
        if (!item || typeof item !== "object" || Array.isArray(item)) {
            return;
        }

        collectResponseFields(item).forEach((field) => {
            if (!fieldMap.has(field.path)) {
                fieldMap.set(field.path, {
                    path: field.path,
                    type: field.type,
                    sample: field.sample
                });
            }
        });
    });

    return Array.from(fieldMap.values()).sort((left, right) => left.path.localeCompare(right.path));
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
            return [...commonRules, "contains", "minCount", "maxCount", "count"];
        case "object":
            return ["type", "notEmpty"];
        case "number":
            return [
                "equals",
                "notEquals",
                "type",
                "greaterThan",
                "greaterThanOrEqual",
                "lessThan",
                "lessThanOrEqual"
            ];
        case "boolean":
            return ["equals", "notEquals", "type"];
        default:
            return commonRules;
    }
}

function getContainsRulesForFieldType(fieldType) {
    switch (fieldType) {
        case "string":
            return ["equals", "notEquals", "containsText", "startsWith", "endsWith", "notEmpty"];
        case "number":
            return ["equals", "notEquals", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual"];
        case "boolean":
            return ["equals", "notEquals"];
        case "object":
            return ["type", "notEmpty"];
        case "array":
            return ["notEmpty", "minCount", "maxCount", "count"];
        default:
            return ["equals", "notEquals"];
    }
}

function addAssertionDraft() {
    const currentDraft = getCurrentTestDraft();
    const field = getSelectedField();
    const rule = assertionRuleSelect.value;

    if (!currentDraft || !field || !rule) {
        renderResponseStatus("Parse a response body and choose a field first.", true);
        return;
    }

    const valueInput = document.getElementById("assertionValueInput");
    const value = convertAssertionValue(rule, field.type, valueInput);

    currentDraft.assertions.push({
        field: field.path,
        rule,
        value
    });

    renderAssertionDrafts();
    renderTestDraftList();
}

function convertAssertionValue(rule, fieldType, input) {
    if (rule === "contains") {
        const relativeFieldInput = document.getElementById("assertionContainsFieldInput");
        const relativeRuleInput = document.getElementById("assertionContainsRuleInput");
        const relativeValueInput = document.getElementById("assertionContainsValueInput");
        const currentField = getSelectedField();
        const containsFieldOptions = getContainsFieldOptions(currentField?.sample);
        const containsField = containsFieldOptions.find((option) => option.path === relativeFieldInput?.value);
        const containsRule = relativeRuleInput?.value || "equals";

        if (!relativeFieldInput || !relativeRuleInput || !relativeValueInput || !containsField) {
            return {};
        }

        const convertedValue = convertContainsFieldValue(
            containsField,
            containsRule,
            relativeValueInput.value);

        return {
            [relativeFieldInput.value]: containsRule === "equals"
                ? convertedValue
                : { [containsRule]: convertedValue }
        };
    }

    const definition = assertionRuleDefinitions[rule];

    if (definition.valueMode === "select") {
        if (rule === "notEmpty") {
            return input.value === "true";
        }

        return input.value;
    }

    if (definition.valueMode === "number") {
        return Number(input.value);
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

function convertContainsFieldValue(fieldDefinition, rule, value) {
    if (rule === "notEmpty") {
        return value === "true";
    }

    switch (fieldDefinition.type) {
        case "number":
            return Number(value);
        case "boolean":
            return value === "true";
        case "object":
        case "array":
            try {
                return JSON.parse(value);
            } catch {
                return value;
            }
        default:
            return value;
    }
}

function renderAssertionDrafts() {
    assertionList.innerHTML = "";
    const currentDraft = getCurrentTestDraft();

    if (!currentDraft || currentDraft.assertions.length === 0) {
        assertionList.innerHTML = "<p class=\"result-note\">No assertion rules added for this test yet.</p>";
        return;
    }

    currentDraft.assertions.forEach((draft, index) => {
        const item = document.createElement("div");
        item.className = "assertion-draft-item";

        const text = document.createElement("span");
        text.textContent = `${draft.field} -> ${draft.rule}: ${formatSample(draft.value)}`;

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "ghost-button inline-button";
        removeButton.textContent = "Remove";
        removeButton.addEventListener("click", () => {
            currentDraft.assertions.splice(index, 1);
            renderAssertionDrafts();
            renderTestDraftList();
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
    if (result.variables && result.variables.hasSuggestions) {
        analysisContainer.appendChild(renderVariablesCard(result.variables));
    }
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
    const { card, body } = createCard("Parsed request", "What the app extracted from the cURL command.");
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
    body.appendChild(details);
    return card;
}

function renderEnvironmentCard(environment) {
    const { card, body } = createCard("Environment scan", "Checks all loaded environment YAML definitions for an existing URL match before suggesting a new environment file.");
    body.appendChild(createBadgeRow(environment.exists, environment.exists ? "Environment found" : "Environment missing"));

    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Matched environments", environment.matchedEnvironmentNames.length > 0 ? environment.matchedEnvironmentNames.join(", ") : "(none)"));
    details.appendChild(createDetail("Suggested environment name", environment.suggestedName));

    if (environment.suggestedFilePath) {
        details.appendChild(createDetail("Suggested file path", environment.suggestedFilePath));
    }

    body.appendChild(details);

    if (environment.suggestedYaml) {
        body.appendChild(createCopyAction(environment.suggestedYaml, "Copy environment YAML"));
        const preview = document.createElement("pre");
        preview.className = "code-block";
        preview.textContent = environment.suggestedYaml;
        body.appendChild(preview);
    }

    return card;
}

function renderEndpointCard(endpoint) {
    const { card, body } = createCard("Endpoint scan", "Checks whether the endpoint already exists, then generates endpoint YAML including every drafted test and its assertions.");
    body.appendChild(createBadgeRow(endpoint.exists, endpoint.exists ? "Endpoint found" : "Endpoint missing"));

    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Matched environments", endpoint.matchedEnvironmentNames.length > 0 ? endpoint.matchedEnvironmentNames.join(", ") : "(none)"));
    details.appendChild(createDetail("Suggested endpoint name", endpoint.suggestedName));

    if (endpoint.suggestedFilePath) {
        details.appendChild(createDetail("Suggested file path", endpoint.suggestedFilePath));
    }

    body.appendChild(details);

    if (endpoint.suggestedYaml) {
        body.appendChild(createCopyAction(endpoint.suggestedYaml, "Copy endpoint YAML"));
        const preview = document.createElement("pre");
        preview.className = "code-block";
        preview.textContent = endpoint.suggestedYaml;
        body.appendChild(preview);
    }

    return card;
}

function createCard(title, summary) {
    const card = document.createElement("details");
    card.className = "preview-card collapsible-preview-card";
    card.open = true;

    const header = document.createElement("summary");
    header.className = "preview-card-summary";
    header.innerHTML = `<h2>${escapeHtml(title)}</h2><p class="result-note">${escapeHtml(summary)}</p>`;

    const body = document.createElement("div");
    body.className = "preview-card-body";

    card.appendChild(header);
    card.appendChild(body);

    return { card, body };
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

function renderVariablesCard(variables) {
    const summary = variables.includedInEnvironmentYaml
        ? "Suggested variables detected from the cURL request. They are already included in the generated environment YAML below."
        : "Suggested variables detected from the cURL request. Paste this block into an existing environment YAML file.";

    const { card, body } = createCard("Variable suggestions", summary);
    body.appendChild(createBadgeRow(true, `${variables.variableNames.length} variables suggested`));

    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Variable names", variables.variableNames.join(", ")));
    body.appendChild(details);

    if (variables.suggestedYaml) {
        body.appendChild(createCopyAction(variables.suggestedYaml, "Copy variables YAML"));
        const preview = document.createElement("pre");
        preview.className = "code-block";
        preview.textContent = variables.suggestedYaml;
        body.appendChild(preview);
    }

    return card;
}

function renderResponseStatus(message, isError) {
    responseStatus.textContent = message;
    responseStatus.classList.toggle("status-error", Boolean(isError));
}

function setBusy(isBusy) {
    analyzeButton.disabled = isBusy;
    addAssertionButton.disabled = isBusy || parsedResponseFields.length === 0 || !getCurrentTestDraft();
    addTestButton.disabled = isBusy;
    testNameInput.disabled = isBusy;
    expectedStatusInput.disabled = isBusy;
    formatResponseButton.disabled = isBusy;
    toggleResponseWrapButton.disabled = isBusy;
    analyzeButton.innerHTML = isBusy
        ? "<i class=\"fa-solid fa-spinner fa-spin button-icon\"></i>Analyzing..."
        : "<i class=\"fa-solid fa-wand-magic-sparkles button-icon\"></i>Analyze and Generate";
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

function normalizeExpectedStatus(value) {
    const parsed = Number.parseInt(value, 10);
    return Number.isNaN(parsed) || parsed <= 0 ? 200 : parsed;
}

function updateCurrentTestDraft(mutator) {
    const currentDraft = getCurrentTestDraft();
    if (!currentDraft) {
        return;
    }

    mutator(currentDraft);
    renderTestDraftList();
}

assertionFieldSelect.addEventListener("change", () => {
    renderRuleOptions();
    renderValueInput();
});

assertionRuleSelect.addEventListener("change", renderValueInput);

addAssertionButton.addEventListener("click", addAssertionDraft);

addTestButton.addEventListener("click", () => {
    const newDraft = createTestDraft();
    testDrafts.push(newDraft);
    currentTestDraftId = newDraft.id;
    renderAssertionBuilder();
});

testNameInput.addEventListener("input", () => {
    updateCurrentTestDraft((draft) => {
        draft.name = testNameInput.value;
    });
});

expectedStatusInput.addEventListener("input", () => {
    updateCurrentTestDraft((draft) => {
        draft.expectedStatus = normalizeExpectedStatus(expectedStatusInput.value);
    });
});

analyzeButton.addEventListener("click", analyzeCurlCommand);
formatResponseButton.addEventListener("click", formatResponseBody);
toggleResponseWrapButton.addEventListener("click", toggleResponseWrap);
responseBodyInput.addEventListener("blur", parseResponseBody);

renderAssertionBuilder();
