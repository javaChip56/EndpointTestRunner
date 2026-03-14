const analyzeButton = document.getElementById("analyzeButton");
const analyzeStatus = document.getElementById("analyzeStatus");
const curlInput = document.getElementById("curlInput");
const analysisContainer = document.getElementById("analysisContainer");

async function analyzeCurlCommand() {
    const command = curlInput.value.trim();
    if (!command) {
        renderStatus("Paste a cURL command first.");
        return;
    }

    setBusy(true);

    try {
        const response = await fetch("/api/tools/curl/analyze", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ command })
        });

        if (!response.ok) {
            throw new Error(`Analyze request failed with status ${response.status}`);
        }

        const result = await response.json();
        renderResult(result);
        renderStatus("Analysis completed.");
    } catch (error) {
        analysisContainer.innerHTML = "";
        renderStatus(error.message || "Unable to analyze the provided cURL command.");
    } finally {
        setBusy(false);
    }
}

function renderResult(result) {
    analysisContainer.innerHTML = "";

    analysisContainer.appendChild(renderRequestCard(result.request));
    analysisContainer.appendChild(renderEnvironmentCard(result.environment));
    analysisContainer.appendChild(renderEndpointCard(result.endpoint));
}

function renderRequestCard(request) {
    const card = createCard("Parsed request", "What the app extracted from the cURL command.");
    const details = document.createElement("dl");
    details.className = "detail-list";
    details.appendChild(createDetail("Method", request.method));
    details.appendChild(createDetail("Base URL", request.baseUrl));
    details.appendChild(createDetail("Path", request.path));
    details.appendChild(createDetail("URL", request.url));
    details.appendChild(createDetail("Query", request.query && Object.keys(request.query).length > 0 ? JSON.stringify(request.query, null, 2) : "(none)"));
    details.appendChild(createDetail("Headers", request.headers && Object.keys(request.headers).length > 0 ? JSON.stringify(request.headers, null, 2) : "(none)"));
    details.appendChild(createDetail("Body", formatBody(request.body, request.rawBody)));
    card.appendChild(details);
    return card;
}

function renderEnvironmentCard(environment) {
    const card = createCard("Environment scan", "Checks whether a matching base URL already exists in the configured environment YAML files.");
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
        const preview = document.createElement("pre");
        preview.className = "code-block";
        preview.textContent = environment.suggestedYaml;
        card.appendChild(preview);
    }

    return card;
}

function renderEndpointCard(endpoint) {
    const card = createCard("Endpoint scan", "Checks whether a matching method and path already exist for the detected environment.");
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

function formatBody(body, rawBody) {
    if (body === null || typeof body === "undefined") {
        return "(none)";
    }

    if (typeof body === "string") {
        return body;
    }

    return JSON.stringify(body, null, 2) || rawBody || "(none)";
}

function renderStatus(message) {
    analyzeStatus.textContent = message;
}

function setBusy(isBusy) {
    analyzeButton.disabled = isBusy;
    analyzeButton.textContent = isBusy ? "Analyzing..." : "Analyze Command";
}

function escapeHtml(value) {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;");
}

analyzeButton.addEventListener("click", analyzeCurlCommand);
