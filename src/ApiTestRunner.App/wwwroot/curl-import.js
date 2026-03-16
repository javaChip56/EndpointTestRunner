const analyzeButton = document.getElementById("analyzeButton");
const analyzeStatus = document.getElementById("analyzeStatus");
const responseStatus = document.getElementById("responseStatus");
const addAssertionButton = document.getElementById("addAssertionButton");
const curlInput = document.getElementById("curlInput");
const responseBodyInput = document.getElementById("responseBodyInput");
const formatResponseButton = document.getElementById("formatResponseButton");
const toggleResponseWrapButton = document.getElementById("toggleResponseWrapButton");
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
        assertionDrafts = assertionDrafts.filter((draft) =>
            parsedResponseFields.some((field) => field.path === draft.field));
        lastParsedResponseBody = responseBody;
        renderAssertionBuilder();
        renderResponseStatus(`${parsedResponseFields.length} selectable fields detected.`, false);
    } catch (error) {
        parsedResponseFields = [];
        parsedResponseObject = null;
        assertionDrafts = [];
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

function setBusy(isBusy) {
    analyzeButton.disabled = isBusy;
    addAssertionButton.disabled = isBusy || parsedResponseFields.length === 0;
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

assertionFieldSelect.addEventListener("change", () => {
    renderRuleOptions();
    renderValueInput();
});
assertionRuleSelect.addEventListener("change", renderValueInput);
addAssertionButton.addEventListener("click", addAssertionDraft);
analyzeButton.addEventListener("click", analyzeCurlCommand);
formatResponseButton.addEventListener("click", formatResponseBody);
toggleResponseWrapButton.addEventListener("click", toggleResponseWrap);
responseBodyInput.addEventListener("blur", parseResponseBody);

renderAssertionBuilder();
