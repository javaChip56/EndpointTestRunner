const analyzeButton = document.getElementById("analyzeButton");
const analyzeStatus = document.getElementById("analyzeStatus");
const responseStatus = document.getElementById("responseStatus");
const addAssertionButton = document.getElementById("addAssertionButton");
const addTestButton = document.getElementById("addTestButton");
const curlInput = document.getElementById("curlInput");
const responseBodyInput = document.getElementById("responseBodyInput");
const formatResponseButton = document.getElementById("formatResponseButton");
const toggleResponseWrapButton = document.getElementById("toggleResponseWrapButton");
const endpointNameInput = document.getElementById("endpointNameInput");
const testNameInput = document.getElementById("testNameInput");
const expectedStatusInput = document.getElementById("expectedStatusInput");
const testDraftList = document.getElementById("testDraftList");
const assertionBuilderGrid = document.getElementById("assertionBuilderGrid");
const assertionFieldSelect = document.getElementById("assertionFieldSelect");
const assertionRuleSelect = document.getElementById("assertionRuleSelect");
const assertionValueContainer = document.getElementById("assertionValueContainer");
const assertionList = document.getElementById("assertionList");
const analysisContainer = document.getElementById("analysisContainer");
const saveEndpointButton = document.getElementById("saveEndpointButton");

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
let editorContext = null;
let busyAction = null;
let currentAssertionEditIndex = null;
let currentAssertionEditTestId = null;

async function analyzeCurlCommand() {
    const command = curlInput.value.trim();
    if (!command) {
        renderStatus("Paste a cURL command first.", true);
        return;
    }

    parseResponseBody();
    setBusy(true, "analyze");

    try {
        const response = await fetch("/api/tools/curl/analyze", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                command,
                environmentId: editorContext?.environmentId || null,
                endpointName: endpointNameInput.value.trim() || null,
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

async function loadEditorSeedFromQuery() {
    const query = new URLSearchParams(window.location.search);
    const environmentId = query.get("environmentId");
    const endpointId = query.get("endpointId");

    if (!environmentId || !endpointId) {
        return;
    }

    setBusy(true, "load");
    renderStatus("Loading endpoint into editor...", false);

    try {
        const response = await fetch(`/api/dashboard/editor-seed?environmentId=${encodeURIComponent(environmentId)}&endpointId=${encodeURIComponent(endpointId)}`, {
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error(await buildErrorMessage(response, "Unable to load endpoint for editing"));
        }

        const seed = await response.json();
        applyEditorSeed(seed);
        renderStatus(`Loaded endpoint from ${seed.environmentName}.`, false);
        await analyzeCurlCommand();
    } catch (error) {
        renderStatus(error.message || "Unable to load endpoint for editing.", true);
    } finally {
        setBusy(false);
    }
}

function applyEditorSeed(seed) {
    curlInput.value = seed.curlCommand || "";
    endpointNameInput.value = seed.endpointName || "";
    responseBodyInput.value = "";
    parsedResponseFields = [];
    parsedResponseObject = null;
    lastParsedResponseBody = "";
    resetAssertionEditState();
    editorContext = seed.sourceFilePath
        ? {
            environmentId: seed.environmentId,
            endpointId: seed.endpointId,
            environmentName: seed.environmentName,
            sourceFilePath: seed.sourceFilePath
        }
        : null;
    testDrafts = (seed.tests || []).map((test, index) => ({
        id: `seed-test-${index + 1}-${Date.now()}`,
        name: test.name || `Test ${index + 1}`,
        expectedStatus: normalizeExpectedStatus(test.expectedStatus),
        assertions: Array.isArray(test.assertions)
            ? test.assertions.map((assertion) => ({
                field: assertion.field,
                rule: assertion.rule,
                value: assertion.value
            }))
            : []
    }));
    nextTestDraftNumber = Math.max(testDrafts.length + 1, nextTestDraftNumber);
    currentTestDraftId = testDrafts[0]?.id ?? null;
    analysisContainer.innerHTML = "";
    renderAssertionBuilder(null);
    renderResponseStatus("Paste a response body if you want to edit assertions by field picker.", false);
    updateSaveButtonState();
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

async function saveEditedEndpoint() {
    if (!editorContext) {
        renderStatus("Open an existing endpoint from the dashboard before saving.", true);
        return;
    }

    const command = curlInput.value.trim();
    if (!command) {
        renderStatus("Paste a cURL command first.", true);
        return;
    }

    if (!endpointNameInput.value.trim()) {
        renderStatus("Provide an endpoint name before saving.", true);
        return;
    }

    setBusy(true, "save");

    try {
        const response = await fetch("/api/dashboard/editor-save", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                environmentId: editorContext.environmentId,
                endpointId: editorContext.endpointId,
                endpointName: endpointNameInput.value.trim(),
                command,
                tests: buildAnalyzePayloadTests()
            })
        });

        if (!response.ok) {
            throw new Error(await buildErrorMessage(response, "Save request failed"));
        }

        const result = await response.json();
        editorContext = {
            environmentId: result.environmentId,
            endpointId: result.endpointId,
            sourceFilePath: result.filePath
        };
        updateEditorQueryString(result.environmentId, result.endpointId);
        renderStatus(`Saved endpoint YAML to ${result.filePath}.`, false);
        await analyzeCurlCommand();
    } catch (error) {
        renderStatus(error.message || "Unable to save the edited endpoint.", true);
    } finally {
        setBusy(false);
    }
}

function updateEditorQueryString(environmentId, endpointId) {
    const url = new URL(window.location.href);
    url.searchParams.set("environmentId", environmentId);
    url.searchParams.set("endpointId", endpointId);
    window.history.replaceState({}, "", url);
}

function updateSaveButtonState() {
    const isEditable = Boolean(editorContext && editorContext.environmentId && editorContext.endpointId);
    saveEndpointButton.hidden = !isEditable;
    saveEndpointButton.disabled = !isEditable || busyAction !== null;
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

function renderAssertionBuilder(editorState = undefined) {
    ensureAtLeastOneTestDraft();
    synchronizeAssertionEditState();
    const nextEditorState = editorState !== undefined
        ? editorState
        : captureAssertionBuilderState();
    renderTestDraftList();
    syncCurrentTestInputs();
    renderFieldOptions();
    restoreAssertionBuilderState(nextEditorState);
    renderAssertionDrafts();
    updateAssertionActionButtons();
}

function ensureAtLeastOneTestDraft() {
    if (testDrafts.length === 0) {
        const initialDraft = createTestDraft();
        testDrafts.push(initialDraft);
        currentTestDraftId = initialDraft.id;
        resetAssertionEditState();
        return;
    }

    if (!testDrafts.some((draft) => draft.id === currentTestDraftId)) {
        currentTestDraftId = testDrafts[0].id;
        resetAssertionEditState();
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
        item.className = "test-draft-item card card-outline card-light";

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
        editButton.className = `btn btn-sm ${draft.id === currentTestDraftId ? "btn-primary is-active" : "btn-default"}`;
        editButton.textContent = draft.id === currentTestDraftId ? "Editing" : "Edit";
        editButton.addEventListener("click", () => {
            currentTestDraftId = draft.id;
            resetAssertionEditState();
            renderAssertionBuilder(null);
        });

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "btn btn-default btn-sm";
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

    resetAssertionEditState();
    renderAssertionBuilder(null);
}

function captureAssertionBuilderState() {
    if (assertionFieldSelect.disabled || assertionRuleSelect.disabled) {
        return null;
    }

    return captureAssertionEditorState(
        assertionFieldSelect.value,
        assertionRuleSelect.value,
        getAssertionControlIds("assertion"));
}

function captureAssertionEditorState(fieldValue, ruleValue, controlIds) {
    const state = {
        field: fieldValue,
        rule: ruleValue
    };

    if (!state.field || !state.rule) {
        return null;
    }

    if (state.rule === "contains") {
        state.containsField = document.getElementById(controlIds.containsFieldInput)?.value ?? "";
        state.containsRule = document.getElementById(controlIds.containsRuleInput)?.value ?? "";
        state.containsValue = document.getElementById(controlIds.containsValueInput)?.value ?? "";
        return state;
    }

    state.value = document.getElementById(controlIds.valueInput)?.value ?? "";
    return state;
}

function restoreAssertionBuilderState(state) {
    renderRuleOptions();

    if (!state) {
        renderValueInput();
        return;
    }

    if (hasSelectOption(assertionFieldSelect, state.field)) {
        assertionFieldSelect.value = state.field;
    }

    renderRuleOptions();

    if (hasSelectOption(assertionRuleSelect, state.rule)) {
        assertionRuleSelect.value = state.rule;
    }

    renderValueInput();
    applyAssertionBuilderValues(state);
}

function applyAssertionBuilderValues(state) {
    if (!state) {
        return;
    }

    if (state.rule === "contains") {
        const containsFieldInput = document.getElementById("assertionContainsFieldInput");
        const containsRuleInput = document.getElementById("assertionContainsRuleInput");
        const containsValueInput = document.getElementById("assertionContainsValueInput");

        if (containsFieldInput && hasSelectOption(containsFieldInput, state.containsField)) {
            containsFieldInput.value = state.containsField;
        }

        if (containsRuleInput && hasSelectOption(containsRuleInput, state.containsRule)) {
            containsRuleInput.value = state.containsRule;
        }

        if (containsValueInput && typeof state.containsValue === "string") {
            containsValueInput.value = state.containsValue;
        }

        renderValueInput();

        const refreshedContainsFieldInput = document.getElementById("assertionContainsFieldInput");
        const refreshedContainsRuleInput = document.getElementById("assertionContainsRuleInput");
        const refreshedContainsValueInput = document.getElementById("assertionContainsValueInput");

        if (refreshedContainsFieldInput && hasSelectOption(refreshedContainsFieldInput, state.containsField)) {
            refreshedContainsFieldInput.value = state.containsField;
        }

        if (refreshedContainsRuleInput && hasSelectOption(refreshedContainsRuleInput, state.containsRule)) {
            refreshedContainsRuleInput.value = state.containsRule;
        }

        if (refreshedContainsValueInput && typeof state.containsValue === "string") {
            refreshedContainsValueInput.value = state.containsValue;
        }

        return;
    }

    const valueInput = document.getElementById("assertionValueInput");
    if (valueInput && typeof state.value === "string") {
        valueInput.value = state.value;
    }
}

function hasSelectOption(selectElement, value) {
    return Boolean(selectElement) && Array.from(selectElement.options).some((option) => option.value === value);
}

function synchronizeAssertionEditState() {
    const currentDraft = getCurrentTestDraft();
    if (!currentDraft ||
        currentAssertionEditTestId !== currentDraft.id ||
        currentAssertionEditIndex === null ||
        currentAssertionEditIndex >= currentDraft.assertions.length) {
        resetAssertionEditState();
    }
}

function isEditingAssertion() {
    const currentDraft = getCurrentTestDraft();
    return Boolean(currentDraft) &&
        currentAssertionEditIndex !== null &&
        currentAssertionEditTestId === currentDraft.id &&
        currentAssertionEditIndex < currentDraft.assertions.length;
}

function resetAssertionEditState() {
    currentAssertionEditIndex = null;
    currentAssertionEditTestId = null;
}

function updateAssertionActionButtons() {
    const canEditAssertions = parsedResponseFields.length > 0 && Boolean(getCurrentTestDraft()) && busyAction === null;
    const isEditing = isEditingAssertion();

    addAssertionButton.disabled = !canEditAssertions || isEditing;
    addAssertionButton.innerHTML = "<i class=\"fa-solid fa-plus button-icon\"></i>Add Assertion";
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
    populateFieldSelect(assertionFieldSelect);
}

function renderRuleOptions() {
    populateRuleSelect(assertionRuleSelect, getSelectedField()?.type, assertionRuleSelect.value);
}

function renderValueInput() {
    const previousContainsField = document.getElementById("assertionContainsFieldInput")?.value ?? "";
    const previousContainsRule = document.getElementById("assertionContainsRuleInput")?.value ?? "";
    const previousContainsValue = document.getElementById("assertionContainsValueInput")?.value ?? "";
    const field = getSelectedField();
    const rule = assertionRuleSelect.value;

    renderAssertionValueEditor(
        assertionValueContainer,
        field,
        rule,
        getAssertionControlIds("assertion"),
        {
            containsField: previousContainsField,
            containsRule: previousContainsRule,
            containsValue: previousContainsValue
        },
        renderValueInput);
}

function renderAssertionValueEditor(container, field, rule, controlIds, state = {}, onEditorChange = null) {
    container.innerHTML = "";
    const label = document.createElement("span");
    label.textContent = "Value";
    container.appendChild(label);

    const isContainsRule = rule === "contains";
    if (container === assertionValueContainer) {
        assertionBuilderGrid.classList.toggle("has-complex-value", isContainsRule);
        assertionValueContainer.classList.toggle("field-stack-wide", isContainsRule);
    }

    if (!field || !rule) {
        if (container === assertionValueContainer) {
            assertionBuilderGrid.classList.remove("has-complex-value");
            assertionValueContainer.classList.remove("field-stack-wide");
        }

        const input = document.createElement("input");
        input.className = "form-control tool-input-inline";
        input.type = "text";
        input.disabled = true;
        container.appendChild(input);
        return;
    }

    const definition = assertionRuleDefinitions[rule];
    if (rule === "contains") {
        renderContainsValueInput(
            container,
            field,
            state.containsField ?? "",
            state.containsRule ?? "",
            state.containsValue ?? "",
            controlIds,
            onEditorChange);
        return;
    }

    if (definition.valueMode === "select") {
        const select = document.createElement("select");
        select.id = controlIds.valueInput;
        select.className = "form-select tool-select";

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

        if (typeof state.value === "string" && state.value !== "") {
            select.value = state.value;
        }

        container.appendChild(select);
        return;
    }

    const input = document.createElement("input");
    input.id = controlIds.valueInput;
    input.className = "form-control tool-input-inline";

    if (definition.valueMode === "number") {
        input.type = "number";
        input.step = "any";
        input.value = typeof state.value === "string" && state.value !== ""
            ? state.value
            : Array.isArray(field.sample) ? String(field.sample.length) : "1";
    } else {
        input.type = "text";
        input.value = typeof state.value === "string"
            ? state.value
            : definition.valueMode === "typed" ? formatSample(field.sample) : "";
    }

    container.appendChild(input);
}

function renderContainsValueInput(container, field, selectedRelativeField, selectedRelativeRule, selectedRelativeValue, controlIds, onEditorChange) {
    const containsFieldOptions = getContainsFieldOptions(field?.sample);

    if (containsFieldOptions.length === 0) {
        const helper = document.createElement("span");
        helper.className = "helper-text";
        helper.textContent = "contains currently supports arrays of objects.";
        container.appendChild(helper);

        const input = document.createElement("input");
        input.className = "form-control tool-input-inline";
        input.type = "text";
        input.disabled = true;
        container.appendChild(input);
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
    select.id = controlIds.containsFieldInput;
    select.className = "form-select tool-select";

    containsFieldOptions.forEach((optionDefinition) => {
        const option = document.createElement("option");
        option.value = optionDefinition.path;
        option.textContent = `${optionDefinition.path} (${optionDefinition.type})`;
        select.appendChild(option);
    });

    if (selectedRelativeField && containsFieldOptions.some((option) => option.path === selectedRelativeField)) {
        select.value = selectedRelativeField;
    }

    select.addEventListener("change", () => onEditorChange?.());

    fieldStack.appendChild(select);
    layout.appendChild(fieldStack);

    const selectedFieldDefinition = containsFieldOptions.find((option) => option.path === select.value) ?? containsFieldOptions[0];
    const ruleStack = document.createElement("label");
    ruleStack.className = "field-stack";

    const ruleLabel = document.createElement("span");
    ruleLabel.textContent = "Match rule";
    ruleStack.appendChild(ruleLabel);

    const ruleSelect = document.createElement("select");
    ruleSelect.id = controlIds.containsRuleInput;
    ruleSelect.className = "form-select tool-select";

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

    ruleSelect.addEventListener("change", () => onEditorChange?.());

    ruleStack.appendChild(ruleSelect);
    layout.appendChild(ruleStack);

    const valueStack = document.createElement("label");
    valueStack.className = "field-stack";

    const valueLabel = document.createElement("span");
    valueLabel.textContent = "Match value";
    valueStack.appendChild(valueLabel);

    const valueInput = createContainsValueInput(selectedFieldDefinition, ruleSelect.value, selectedRelativeValue, controlIds);
    valueStack.appendChild(valueInput);
    layout.appendChild(valueStack);

    container.appendChild(layout);
}

function createContainsValueInput(fieldDefinition, rule, previousValue, controlIds) {
    const definition = assertionRuleDefinitions[rule];

    if (definition.valueMode === "select") {
        const select = document.createElement("select");
        select.id = controlIds.containsValueInput;
        select.className = "form-select tool-select";

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
    input.id = controlIds.containsValueInput;
    input.className = "form-control tool-input-inline";

    if (definition.valueMode === "number") {
        input.type = "number";
        input.step = "any";
    } else {
        input.type = "text";
    }

    input.value = previousValue || formatSample(fieldDefinition.sample);
    return input;
}

function getAssertionControlIds(prefix) {
    return {
        valueInput: `${prefix}ValueInput`,
        containsFieldInput: `${prefix}ContainsFieldInput`,
        containsRuleInput: `${prefix}ContainsRuleInput`,
        containsValueInput: `${prefix}ContainsValueInput`
    };
}

function populateFieldSelect(selectElement) {
    selectElement.innerHTML = "";

    if (parsedResponseFields.length === 0) {
        const option = document.createElement("option");
        option.textContent = "Parse a response body first";
        option.value = "";
        selectElement.appendChild(option);
        selectElement.disabled = true;
        return;
    }

    selectElement.disabled = false;

    for (const field of parsedResponseFields) {
        const option = document.createElement("option");
        option.value = field.path;
        option.textContent = `${field.path} (${field.type})`;
        selectElement.appendChild(option);
    }
}

function populateRuleSelect(selectElement, fieldType, preferredRule) {
    selectElement.innerHTML = "";

    if (!fieldType) {
        selectElement.disabled = true;
        return;
    }

    const supportedRules = getRulesForFieldType(fieldType);
    supportedRules.forEach((rule) => {
        const option = document.createElement("option");
        option.value = rule;
        option.textContent = assertionRuleDefinitions[rule].label;
        selectElement.appendChild(option);
    });

    selectElement.disabled = false;
    if (preferredRule && supportedRules.includes(preferredRule)) {
        selectElement.value = preferredRule;
    }
}

function getFieldDefinitionByPath(path) {
    return parsedResponseFields.find((field) => field.path === path) ?? null;
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

    return getFieldDefinitionByPath(assertionFieldSelect.value) ?? parsedResponseFields[0];
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

    const updatedAssertion = {
        field: field.path,
        rule,
        value
    };

    if (isEditingAssertion()) {
        currentDraft.assertions[currentAssertionEditIndex] = updatedAssertion;
        renderResponseStatus("Assertion updated.", false);
    } else {
        currentDraft.assertions.push(updatedAssertion);
        renderResponseStatus("Assertion added.", false);
    }

    resetAssertionEditState();
    renderAssertionBuilder(null);
}

function convertAssertionValue(rule, fieldType, input, controlIds = getAssertionControlIds("assertion"), currentFieldDefinition = getSelectedField()) {
    if (rule === "contains") {
        const relativeFieldInput = document.getElementById(controlIds.containsFieldInput);
        const relativeRuleInput = document.getElementById(controlIds.containsRuleInput);
        const relativeValueInput = document.getElementById(controlIds.containsValueInput);
        const containsFieldOptions = getContainsFieldOptions(currentFieldDefinition?.sample);
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
        item.className = "assertion-draft-item card card-outline card-light";

        if (isEditingAssertion() && currentAssertionEditIndex === index) {
            item.appendChild(createInlineAssertionEditor(draft, index));
        } else {
            const text = document.createElement("span");
            text.className = "assertion-draft-text";
            text.textContent = `${draft.field} -> ${draft.rule}: ${formatSample(draft.value)}`;

            const actions = document.createElement("div");
            actions.className = "test-draft-actions";

            const editButton = document.createElement("button");
            editButton.type = "button";
            editButton.className = "btn btn-default btn-sm";
            editButton.textContent = "Edit";
            editButton.disabled = isEditingAssertion();
            editButton.addEventListener("click", () => startAssertionEdit(index));

            const removeButton = document.createElement("button");
            removeButton.type = "button";
            removeButton.className = "btn btn-default btn-sm";
            removeButton.textContent = "Remove";
            removeButton.disabled = isEditingAssertion();
            removeButton.addEventListener("click", () => {
                currentDraft.assertions.splice(index, 1);
                if (currentAssertionEditIndex === index) {
                    resetAssertionEditState();
                }
                renderAssertionBuilder(null);
            });

            actions.appendChild(editButton);
            actions.appendChild(removeButton);
            item.appendChild(text);
            item.appendChild(actions);
        }

        assertionList.appendChild(item);
    });
}

function createInlineAssertionEditor(draft, index) {
    const editor = document.createElement("div");
    editor.className = "assertion-inline-editor";

    const fieldsGrid = document.createElement("div");
    fieldsGrid.className = "assertion-inline-grid";

    const fieldStack = document.createElement("label");
    fieldStack.className = "field-stack";
    const fieldLabel = document.createElement("span");
    fieldLabel.textContent = "Field";
    const fieldSelect = document.createElement("select");
    fieldSelect.className = "form-select tool-select";
    populateFieldSelect(fieldSelect);
    if (hasSelectOption(fieldSelect, draft.field)) {
        fieldSelect.value = draft.field;
    }
    fieldStack.appendChild(fieldLabel);
    fieldStack.appendChild(fieldSelect);
    fieldsGrid.appendChild(fieldStack);

    const ruleStack = document.createElement("label");
    ruleStack.className = "field-stack";
    const ruleLabel = document.createElement("span");
    ruleLabel.textContent = "Rule";
    const ruleSelect = document.createElement("select");
    ruleSelect.className = "form-select tool-select";
    ruleStack.appendChild(ruleLabel);
    ruleStack.appendChild(ruleSelect);
    fieldsGrid.appendChild(ruleStack);

    const valueStack = document.createElement("div");
    valueStack.className = "field-stack assertion-inline-value";
    fieldsGrid.appendChild(valueStack);

    const editorState = buildAssertionEditorStateFromDraft(draft);
    const controlIds = {
        valueInput: `inlineAssertionValueInput-${index}`,
        containsFieldInput: `inlineAssertionContainsFieldInput-${index}`,
        containsRuleInput: `inlineAssertionContainsRuleInput-${index}`,
        containsValueInput: `inlineAssertionContainsValueInput-${index}`
    };

    const renderEditorValue = () => {
        const currentEditorState = captureAssertionEditorState(fieldSelect.value, ruleSelect.value, controlIds) ?? editorState;
        const selectedField = getFieldDefinitionByPath(fieldSelect.value);
        populateRuleSelect(ruleSelect, selectedField?.type, currentEditorState.rule);
        renderAssertionValueEditor(
            valueStack,
            selectedField,
            ruleSelect.value,
            controlIds,
            currentEditorState,
            renderEditorValue);
    };

    populateRuleSelect(ruleSelect, getFieldDefinitionByPath(fieldSelect.value)?.type, editorState.rule);
    renderEditorValue();

    fieldSelect.addEventListener("change", () => {
        editorState.field = fieldSelect.value;
        editorState.rule = "";
        renderEditorValue();
    });

    ruleSelect.addEventListener("change", () => {
        editorState.rule = ruleSelect.value;
        renderEditorValue();
    });

    const actions = document.createElement("div");
    actions.className = "test-draft-actions assertion-inline-actions";

    const saveButton = document.createElement("button");
    saveButton.type = "button";
    saveButton.className = "btn btn-primary btn-sm";
    saveButton.textContent = "Save";
    saveButton.addEventListener("click", () => saveInlineAssertionEdit(index, fieldSelect, ruleSelect, controlIds));

    const cancelButton = document.createElement("button");
    cancelButton.type = "button";
    cancelButton.className = "btn btn-default btn-sm";
    cancelButton.textContent = "Cancel";
    cancelButton.addEventListener("click", cancelAssertionEdit);

    actions.appendChild(saveButton);
    actions.appendChild(cancelButton);

    editor.appendChild(fieldsGrid);
    editor.appendChild(actions);
    return editor;
}

function saveInlineAssertionEdit(index, fieldSelect, ruleSelect, controlIds) {
    const currentDraft = getCurrentTestDraft();
    const selectedField = getFieldDefinitionByPath(fieldSelect.value);
    const selectedRule = ruleSelect.value;
    const valueInput = document.getElementById(
        selectedRule === "contains" ? controlIds.containsValueInput : controlIds.valueInput);

    if (!currentDraft || !selectedField || !selectedRule || !valueInput) {
        renderResponseStatus("Choose a field and rule before saving the assertion.", true);
        return;
    }

    currentDraft.assertions[index] = {
        field: selectedField.path,
        rule: selectedRule,
        value: convertAssertionValue(selectedRule, selectedField.type, valueInput, controlIds, selectedField)
    };

    resetAssertionEditState();
    renderAssertionBuilder(null);
    renderResponseStatus("Assertion updated.", false);
}

function startAssertionEdit(index) {
    const currentDraft = getCurrentTestDraft();
    const assertion = currentDraft?.assertions[index];

    if (!currentDraft || !assertion) {
        return;
    }

    if (parsedResponseFields.length === 0) {
        renderResponseStatus("Paste and parse a response body before editing an assertion.", true);
        return;
    }

    currentAssertionEditIndex = index;
    currentAssertionEditTestId = currentDraft.id;
    renderAssertionBuilder(captureAssertionBuilderState());
    renderResponseStatus("Editing assertion inline. Save or cancel from the assertion row.", false);
}

function cancelAssertionEdit() {
    if (!isEditingAssertion()) {
        return;
    }

    resetAssertionEditState();
    renderAssertionBuilder(captureAssertionBuilderState());
    renderResponseStatus("Assertion edit cancelled.", false);
}

function buildAssertionEditorStateFromDraft(draft) {
    const state = {
        field: draft.field,
        rule: draft.rule
    };

    if (draft.rule === "contains") {
        const [containsField, containsDefinition] = Object.entries(draft.value ?? {})[0] ?? [];
        state.containsField = containsField ?? "";

        if (containsDefinition &&
            typeof containsDefinition === "object" &&
            !Array.isArray(containsDefinition)) {
            const [containsRule, containsValue] = Object.entries(containsDefinition)[0] ?? [];
            if (containsRule && assertionRuleDefinitions[containsRule]) {
                state.containsRule = containsRule;
                state.containsValue = stringifyAssertionEditorValue(containsRule, containsValue);
                return state;
            }
        }

        state.containsRule = "equals";
        state.containsValue = stringifyAssertionEditorValue("equals", containsDefinition);
        return state;
    }

    state.value = stringifyAssertionEditorValue(draft.rule, draft.value);
    return state;
}

function stringifyAssertionEditorValue(rule, value) {
    if (value === null || typeof value === "undefined") {
        return "";
    }

    if (rule === "notEmpty") {
        return value === true ? "true" : "false";
    }

    return typeof value === "string"
        ? value
        : formatSample(value);
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
    card.className = "preview-card card card-outline card-warning warning-card";
    card.innerHTML = "<div class=\"card-header\"><h2 class=\"card-title\">Warnings</h2></div><div class=\"card-body\"><p class=\"result-note mb-0\">The analyzer continued with generated suggestions even though the configured YAML suite could not be loaded fully.</p></div>";

    const list = document.createElement("ul");
    list.className = "warning-list";

    warnings.forEach((warning) => {
        const item = document.createElement("li");
        item.textContent = warning;
        list.appendChild(item);
    });

    card.querySelector(".card-body").appendChild(list);
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
    const { card, body } = createCard("Environment scan", buildEnvironmentSummary(environment));
    body.appendChild(createMatchBadgeRow(environment.matchStatus, getEnvironmentStatusLabel(environment)));

    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Matched environments", environment.matchedEnvironmentNames.length > 0 ? environment.matchedEnvironmentNames.join(", ") : "(none)"));
    details.appendChild(createDetail("Suggested environment name", environment.suggestedName));

    if (environment.suggestedFilePath) {
        details.appendChild(createDetail("Suggested file path", environment.suggestedFilePath));
    }

    body.appendChild(details);

    if (environment.candidates && environment.candidates.length > 0) {
        body.appendChild(createCandidateList(
            "Matched candidates",
            environment.candidates,
            (candidate) => `${candidate.name} -> ${candidate.baseUrl} (relative path ${candidate.relativePath})`));
    }

    if (environment.suggestedYaml) {
        if (environment.currentYaml) {
            body.appendChild(createCodeSection("Current environment YAML", environment.currentYaml));
        }
        if (environment.currentYaml && environment.suggestedYaml) {
            body.appendChild(createInlineDiffSection("Inline diff", environment.currentYaml, environment.suggestedYaml));
        } else if (environment.diffYaml) {
            body.appendChild(createCodeSection("Diff preview", environment.diffYaml, "code-block diff-block"));
        }
        body.appendChild(createCopyAction(
            environment.suggestedYaml,
            environment.matchStatus === "matched" ? "Copy updated environment YAML" : "Copy environment YAML"));
        body.appendChild(createCodeSection(
            environment.matchStatus === "matched" ? "Updated environment YAML" : "Suggested environment YAML",
            environment.suggestedYaml));
    }

    return card;
}

function renderEndpointCard(endpoint) {
    const { card, body } = createCard("Endpoint scan", buildEndpointSummary(endpoint));
    body.appendChild(createMatchBadgeRow(endpoint.matchStatus, getEndpointStatusLabel(endpoint)));

    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Matched environments", endpoint.matchedEnvironmentNames.length > 0 ? endpoint.matchedEnvironmentNames.join(", ") : "(none)"));
    details.appendChild(createDetail("Suggested endpoint name", endpoint.suggestedName));

    if (endpoint.suggestedFilePath) {
        details.appendChild(createDetail("Suggested file path", endpoint.suggestedFilePath));
    }

    body.appendChild(details);

    if (endpoint.candidates && endpoint.candidates.length > 0) {
        body.appendChild(createCandidateList(
            "Matched candidates",
            endpoint.candidates,
            (candidate) => `${candidate.name} -> ${candidate.method} ${candidate.path} (${candidate.environmentNames.join(", ")})`));
    }

    if (endpoint.suggestedYaml) {
        if (endpoint.currentYaml) {
            body.appendChild(createCodeSection("Current endpoint YAML", endpoint.currentYaml));
        }
        if (endpoint.currentYaml && endpoint.suggestedYaml) {
            body.appendChild(createInlineDiffSection("Inline diff", endpoint.currentYaml, endpoint.suggestedYaml));
        } else if (endpoint.diffYaml) {
            body.appendChild(createCodeSection("Diff preview", endpoint.diffYaml, "code-block diff-block"));
        }
        body.appendChild(createCopyAction(
            endpoint.suggestedYaml,
            endpoint.matchStatus === "matched" ? "Copy updated endpoint YAML" : "Copy endpoint YAML"));
        body.appendChild(createCodeSection(
            endpoint.matchStatus === "matched" ? "Updated endpoint YAML" : "Suggested endpoint YAML",
            endpoint.suggestedYaml));
    }

    const entries = [];
    let beforeIndex = 0;
    let afterIndex = 0;

    while (beforeIndex < beforeLines.length && afterIndex < afterLines.length) {
        if (beforeLines[beforeIndex] === afterLines[afterIndex]) {
            entries.push({ type: "unchanged", prefix: " ", text: beforeLines[beforeIndex] });
            beforeIndex += 1;
            afterIndex += 1;
            continue;
        }

        if (lengths[beforeIndex + 1][afterIndex] >= lengths[beforeIndex][afterIndex + 1]) {
            entries.push({ type: "removed", prefix: "-", text: beforeLines[beforeIndex] });
            beforeIndex += 1;
        } else {
            entries.push({ type: "added", prefix: "+", text: afterLines[afterIndex] });
            afterIndex += 1;
        }
    }

    while (beforeIndex < beforeLines.length) {
        entries.push({ type: "removed", prefix: "-", text: beforeLines[beforeIndex] });
        beforeIndex += 1;
    }

    while (afterIndex < afterLines.length) {
        entries.push({ type: "added", prefix: "+", text: afterLines[afterIndex] });
        afterIndex += 1;
    }

    return entries;
}

function normalizeDiffLines(text) {
    return (text || "")
        .replace(/\r\n/g, "\n")
        .split("\n");
}

function createCard(title, summary) {
    const card = document.createElement("details");
    card.className = "preview-card collapsible-preview-card card card-outline card-secondary";
    card.open = true;

    const header = document.createElement("summary");
    header.className = "card-header preview-card-summary";
    header.innerHTML = `<div><h2 class="card-title">${escapeHtml(title)}</h2><p class="result-note mb-0">${escapeHtml(summary)}</p></div>`;

    const body = document.createElement("div");
    body.className = "card-body preview-card-body";

    card.appendChild(header);
    card.appendChild(body);

    return { card, body };
}

function createBadgeRow(isPassing, text) {
    const wrapper = document.createElement("div");
    wrapper.className = "badge-row";

    const badge = document.createElement("span");
    badge.className = `status-badge badge rounded-pill ${isPassing ? "text-bg-success" : "text-bg-danger"}`;
    badge.textContent = text;

    wrapper.appendChild(badge);
    return wrapper;
}

function createMatchBadgeRow(matchStatus, text) {
    const wrapper = document.createElement("div");
    wrapper.className = "badge-row";

    const badge = document.createElement("span");
    const statusClass = matchStatus === "matched"
        ? "passing"
        : matchStatus === "ambiguous"
            ? "warning"
            : "failing";
    badge.className = `status-badge ${statusClass}`;
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
    button.className = "btn btn-default btn-sm copy-button";
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

function createCodeSection(title, text, codeClassName = "code-block") {
    const wrapper = document.createElement("section");
    wrapper.className = "preview-code-section";

    const heading = document.createElement("h3");
    heading.className = "preview-code-title";
    heading.textContent = title;

    const preview = document.createElement("pre");
    preview.className = codeClassName;
    preview.textContent = text;

    wrapper.appendChild(heading);
    wrapper.appendChild(preview);
    return wrapper;
}

function createInlineDiffSection(title, currentText, updatedText) {
    const wrapper = document.createElement("section");
    wrapper.className = "preview-code-section";

    const heading = document.createElement("h3");
    heading.className = "preview-code-title";
    heading.textContent = title;

    const preview = document.createElement("pre");
    preview.className = "code-block inline-diff-block";
    preview.innerHTML = buildInlineDiffHtml(currentText, updatedText);

    wrapper.appendChild(heading);
    wrapper.appendChild(preview);
    return wrapper;
}

function buildInlineDiffHtml(currentText, updatedText) {
    const currentLines = normalizeDiffText(currentText).split("\n");
    const updatedLines = normalizeDiffText(updatedText).split("\n");
    const rows = buildLineDiffRows(currentLines, updatedLines);

    return rows.map((row) => {
        if (row.type === "context") {
            return `<div class="diff-line diff-line-context"><span class="diff-marker"> </span><span class="diff-content">${escapeHtml(row.text)}</span></div>`;
        }

        if (row.type === "remove") {
            return `<div class="diff-line diff-line-remove"><span class="diff-marker">-</span><span class="diff-content">${escapeHtml(row.text)}</span></div>`;
        }

        if (row.type === "add") {
            return `<div class="diff-line diff-line-add"><span class="diff-marker">+</span><span class="diff-content">${escapeHtml(row.text)}</span></div>`;
        }

        const removedHtml = renderInlineSegments(row.removedSegments, "remove");
        const addedHtml = renderInlineSegments(row.addedSegments, "add");

        return [
            `<div class="diff-line diff-line-remove"><span class="diff-marker">-</span><span class="diff-content">${removedHtml}</span></div>`,
            `<div class="diff-line diff-line-add"><span class="diff-marker">+</span><span class="diff-content">${addedHtml}</span></div>`
        ].join("");
    }).join("");
}

function buildLineDiffRows(currentLines, updatedLines) {
    const lcs = buildLcsMatrix(currentLines, updatedLines);
    const rows = [];
    let currentIndex = 0;
    let updatedIndex = 0;

    while (currentIndex < currentLines.length && updatedIndex < updatedLines.length) {
        if (currentLines[currentIndex] === updatedLines[updatedIndex]) {
            rows.push({ type: "context", text: currentLines[currentIndex] });
            currentIndex++;
            updatedIndex++;
            continue;
        }

        const removeScore = lcs[currentIndex + 1][updatedIndex];
        const addScore = lcs[currentIndex][updatedIndex + 1];

        if (removeScore === addScore && currentIndex + 1 <= currentLines.length && updatedIndex + 1 <= updatedLines.length) {
            rows.push({
                type: "modify",
                removedSegments: buildInlineSegments(currentLines[currentIndex], updatedLines[updatedIndex], "remove"),
                addedSegments: buildInlineSegments(currentLines[currentIndex], updatedLines[updatedIndex], "add")
            });
            currentIndex++;
            updatedIndex++;
            continue;
        }

        if (removeScore >= addScore) {
            rows.push({ type: "remove", text: currentLines[currentIndex] });
            currentIndex++;
        } else {
            rows.push({ type: "add", text: updatedLines[updatedIndex] });
            updatedIndex++;
        }
    }

    while (currentIndex < currentLines.length) {
        rows.push({ type: "remove", text: currentLines[currentIndex] });
        currentIndex++;
    }

    while (updatedIndex < updatedLines.length) {
        rows.push({ type: "add", text: updatedLines[updatedIndex] });
        updatedIndex++;
    }

    return rows;
}

function buildInlineSegments(currentLine, updatedLine, mode) {
    const currentTokens = tokenizeDiffLine(currentLine);
    const updatedTokens = tokenizeDiffLine(updatedLine);
    const lcs = buildLcsMatrix(currentTokens, updatedTokens);
    const segments = [];
    let currentIndex = 0;
    let updatedIndex = 0;

    while (currentIndex < currentTokens.length && updatedIndex < updatedTokens.length) {
        if (currentTokens[currentIndex] === updatedTokens[updatedIndex]) {
            segments.push({ text: mode === "remove" ? currentTokens[currentIndex] : updatedTokens[updatedIndex], changed: false });
            currentIndex++;
            updatedIndex++;
            continue;
        }

        if (lcs[currentIndex + 1][updatedIndex] >= lcs[currentIndex][updatedIndex + 1]) {
            if (mode === "remove") {
                segments.push({ text: currentTokens[currentIndex], changed: true });
            }
            currentIndex++;
        } else {
            if (mode === "add") {
                segments.push({ text: updatedTokens[updatedIndex], changed: true });
            }
            updatedIndex++;
        }
    }

    while (currentIndex < currentTokens.length) {
        if (mode === "remove") {
            segments.push({ text: currentTokens[currentIndex], changed: true });
        }
        currentIndex++;
    }

    while (updatedIndex < updatedTokens.length) {
        if (mode === "add") {
            segments.push({ text: updatedTokens[updatedIndex], changed: true });
        }
        updatedIndex++;
    }

    return segments;
}

function renderInlineSegments(segments, mode) {
    return segments.map((segment) => {
        const safeText = escapeHtml(segment.text);
        return segment.changed
            ? `<span class="diff-change diff-change-${mode}">${safeText}</span>`
            : safeText;
    }).join("");
}

function tokenizeDiffLine(line) {
    return line.match(/(\s+|[^\s]+)/g) ?? [line];
}

function buildLcsMatrix(left, right) {
    const matrix = Array.from({ length: left.length + 1 }, () => Array(right.length + 1).fill(0));

    for (let leftIndex = left.length - 1; leftIndex >= 0; leftIndex -= 1) {
        for (let rightIndex = right.length - 1; rightIndex >= 0; rightIndex -= 1) {
            matrix[leftIndex][rightIndex] = left[leftIndex] === right[rightIndex]
                ? matrix[leftIndex + 1][rightIndex + 1] + 1
                : Math.max(matrix[leftIndex + 1][rightIndex], matrix[leftIndex][rightIndex + 1]);
        }
    }

    return matrix;
}

function normalizeDiffText(value) {
    return String(value ?? "").replaceAll("\r\n", "\n");
}

function createCandidateList(title, candidates, formatCandidate) {
    const wrapper = document.createElement("div");
    wrapper.className = "detail-list";

    const term = document.createElement("dt");
    term.textContent = title;

    const description = document.createElement("dd");
    const list = document.createElement("ul");
    list.className = "candidate-list";

    candidates.forEach((candidate) => {
        const item = document.createElement("li");
        item.textContent = formatCandidate(candidate);
        list.appendChild(item);
    });

    description.appendChild(list);
    wrapper.appendChild(term);
    wrapper.appendChild(description);
    return wrapper;
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

function buildEnvironmentSummary(environment) {
    if (environment.matchStatus === "matched") {
        return "Matched an existing environment by URL. Review the updated YAML block below and merge the changes manually into the right file.";
    }

    if (environment.matchStatus === "ambiguous") {
        return "Multiple existing environments matched this request URL. Review the candidates below and decide which YAML file to update manually.";
    }

    return "No existing environment matched this request URL. A new environment YAML block is suggested below.";
}

function buildEndpointSummary(endpoint) {
    if (endpoint.matchStatus === "matched") {
        return "Matched an existing endpoint by method and path. Review the updated YAML block below and merge the changes manually into the right file.";
    }

    if (endpoint.matchStatus === "ambiguous") {
        return "Multiple existing endpoints matched this request. Review the candidates below and decide which YAML file to update manually.";
    }

    return "No existing endpoint matched this request. A new endpoint YAML block is suggested below.";
}

function getEnvironmentStatusLabel(environment) {
    if (environment.matchStatus === "matched") {
        return "Matched existing environment";
    }

    if (environment.matchStatus === "ambiguous") {
        return "Multiple environment matches";
    }

    return "Environment missing";
}

function getEndpointStatusLabel(endpoint) {
    if (endpoint.matchStatus === "matched") {
        return "Matched existing endpoint";
    }

    if (endpoint.matchStatus === "ambiguous") {
        return "Multiple endpoint matches";
    }

    return "Endpoint missing";
}

function renderResponseStatus(message, isError) {
    responseStatus.textContent = message;
    responseStatus.classList.toggle("status-error", Boolean(isError));
}

function setBusy(isBusy, action = null) {
    busyAction = isBusy ? action : null;
    analyzeButton.disabled = isBusy;
    saveEndpointButton.disabled = isBusy || !editorContext;
    addTestButton.disabled = isBusy;
    endpointNameInput.disabled = isBusy;
    testNameInput.disabled = isBusy;
    expectedStatusInput.disabled = isBusy;
    formatResponseButton.disabled = isBusy;
    toggleResponseWrapButton.disabled = isBusy;
    analyzeButton.innerHTML = isBusy
        ? "<i class=\"fa-solid fa-spinner fa-spin button-icon\"></i>Analyzing..."
        : "<i class=\"fa-solid fa-wand-magic-sparkles button-icon\"></i>Analyze and Generate";
    saveEndpointButton.innerHTML = busyAction === "save"
        ? "<i class=\"fa-solid fa-spinner fa-spin button-icon\"></i>Saving..."
        : "<i class=\"fa-solid fa-floppy-disk button-icon\"></i>Save Endpoint YAML";
    updateSaveButtonState();
    updateAssertionActionButtons();
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
    resetAssertionEditState();
    renderAssertionBuilder(null);
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
saveEndpointButton.addEventListener("click", saveEditedEndpoint);
formatResponseButton.addEventListener("click", formatResponseBody);
toggleResponseWrapButton.addEventListener("click", toggleResponseWrap);
responseBodyInput.addEventListener("blur", parseResponseBody);

renderAssertionBuilder();
updateSaveButtonState();
loadEditorSeedFromQuery();
