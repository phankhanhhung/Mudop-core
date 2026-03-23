using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BMMDL.Registry.Api.Models;

namespace BMMDL.Registry.Api.Services;

public interface IAiService
{
    bool IsConfigured { get; }
    string ModelName { get; }
    Task<AiAssistResponse> AssistAsync(AiAssistRequest request, CancellationToken ct = default);
    Task<NlQueryResponse> NlQueryAsync(NlQueryRequest request, CancellationToken ct = default);
}

public class AiService : IAiService
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly ILogger<AiService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
    public string ModelName => _model;

    public AiService(HttpClient http, IConfiguration configuration, ILogger<AiService> logger)
    {
        _http = http;
        _apiKey = configuration["AI:AnthropicApiKey"];
        _model = configuration["AI:Model"] ?? "claude-haiku-4-5-20251001";
        _logger = logger;

        _http.BaseAddress = new Uri("https://api.anthropic.com/");
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<AiAssistResponse> AssistAsync(AiAssistRequest request, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI:AnthropicApiKey is not configured.");

        var (systemPrompt, userMessage) = BuildPrompts(request);

        var body = new
        {
            model = _model,
            max_tokens = request.Operation == "complete" ? 256 : 1024,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userMessage } }
        };

        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { Content = content };
        httpRequest.Headers.Add("x-api-key", _apiKey!);

        var response = await _http.SendAsync(httpRequest, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic API error {Status}: {Body}", response.StatusCode, responseJson);
            throw new HttpRequestException($"Anthropic API returned {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        // For review operation, split into numbered suggestions
        List<string>? suggestions = null;
        if (request.Operation == "review")
        {
            suggestions = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.TrimStart().Length > 0)
                .ToList();
        }

        return new AiAssistResponse { Result = text.Trim(), Suggestions = suggestions };
    }

    public async Task<NlQueryResponse> NlQueryAsync(NlQueryRequest request, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI:AnthropicApiKey is not configured.");

        var systemPrompt = $"""
            You are an OData v4 query assistant for an enterprise ERP system called BMMDL.
            The user will describe what data they want in plain English, and you will translate it into OData query parameters.
            You MUST respond with ONLY a valid JSON object (no markdown fences, no explanation outside JSON) with these optional fields:
            - "filter": OData $filter expression string (e.g. "Status eq 'Active' and Amount gt 1000")
            - "expand": OData $expand clause (e.g. "Orders,Orders/Lines")
            - "select": OData $select clause (e.g. "ID,Name,Status")
            - "orderby": OData $orderby clause (e.g. "CreatedAt desc")
            - "description": required string — a brief human-readable explanation of what the query returns

            Entity schema:
            {request.SchemaContext}
            """;

        var messages = new List<object>();
        if (request.History != null)
        {
            foreach (var msg in request.History)
            {
                messages.Add(new { role = msg.Role, content = msg.Content });
            }
        }
        messages.Add(new { role = "user", content = request.Prompt });

        var body = new
        {
            model = _model,
            max_tokens = 512,
            system = systemPrompt,
            messages
        };

        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { Content = content };
        httpRequest.Headers.Add("x-api-key", _apiKey!);

        var response = await _http.SendAsync(httpRequest, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic API error {Status}: {Body}", response.StatusCode, responseJson);
            throw new HttpRequestException($"Anthropic API returned {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        // Parse the JSON response from the AI
        try
        {
            using var resultDoc = JsonDocument.Parse(text.Trim());
            var root = resultDoc.RootElement;
            return new NlQueryResponse
            {
                Filter = root.TryGetProperty("filter", out var f) ? f.GetString() : null,
                Expand = root.TryGetProperty("expand", out var e) ? e.GetString() : null,
                Select = root.TryGetProperty("select", out var s) ? s.GetString() : null,
                Orderby = root.TryGetProperty("orderby", out var o) ? o.GetString() : null,
                Description = root.TryGetProperty("description", out var d)
                    ? d.GetString() ?? text.Trim()
                    : text.Trim(),
            };
        }
        catch (JsonException)
        {
            _logger.LogWarning("Failed to parse NL query JSON response: {Text}", text);
            return new NlQueryResponse { Description = text.Trim() };
        }
    }

    private static (string system, string user) BuildPrompts(AiAssistRequest request)
    {
        return request.Operation switch
        {
            "complete" => (
                system: "You are a BMMDL code completion assistant. The user is editing BMMDL DSL code. Given the code context and cursor position (marked with \u25cc or at the end of the provided text), complete the code naturally. Output ONLY the completion text to insert \u2014 no explanation, no markdown fences.",
                user: $"Complete the following BMMDL code at cursor (line {request.CursorLine}, col {request.CursorColumn}):\n\n{request.Context}"
            ),
            "generate" => (
                system: "You are a BMMDL DSL code generation expert. Convert the user's natural language description into well-formed BMMDL DSL code. Include entity definitions, fields, enums, relationships, access control, and rules as appropriate. Output ONLY the BMMDL code block \u2014 no explanation, no markdown fences.",
                user: request.Prompt ?? request.Context
            ),
            "review" => (
                system: "You are a BMMDL schema review expert. Analyze the provided BMMDL schema for:\n- Missing indexes on frequently-queried fields\n- Normalization issues\n- Missing or incomplete relationships\n- Security gaps (missing access control)\n- Naming convention violations\n- Performance considerations\nProvide a numbered list of concrete, actionable suggestions. Be concise \u2014 one suggestion per line.",
                user: $"Review this BMMDL schema:\n\n{request.Context}"
            ),
            "explain-error" => (
                system: "You are a BMMDL debugging assistant. Explain the compilation error in plain English and provide a concrete fix. Be brief (2-4 sentences max). Format: first sentence explains the problem, remaining sentences give the fix.",
                user: $"Error: {request.Error}\n\nBMDL source:\n{request.Context}"
            ),
            _ => throw new ArgumentException($"Unknown operation: {request.Operation}")
        };
    }
}
