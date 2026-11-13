using System.Text;
using System.Text.Json.Nodes;

namespace AiDnD.Core.Llm.Providers;

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.5-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}

/// <summary>
/// Google Gemini через REST API (generateContent) з підтримкою function calling і JSON-схем.
/// Документація: https://ai.google.dev/gemini-api/docs/function-calling
/// </summary>
public sealed class GeminiProvider : ILlmProvider
{
    public const string ProviderName = "gemini";

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiProvider(GeminiOptions options, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new LlmException("Не задано ключ Gemini API (змінна середовища GEMINI_API_KEY або appsettings.json).");
        _options = options;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public string Name => ProviderName;
    public string Model => _options.Model;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt }) },
            ["contents"] = BuildContents(request.Messages),
        };

        var generationConfig = new JsonObject { ["temperature"] = request.Temperature };
        if (request.ResponseSchema is not null)
        {
            generationConfig["responseMimeType"] = "application/json";
            generationConfig["responseSchema"] = request.ResponseSchema.DeepClone();
        }
        body["generationConfig"] = generationConfig;

        if (request.Tools.Count > 0)
        {
            var declarations = new JsonArray();
            foreach (var t in request.Tools)
                declarations.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.Parameters.DeepClone(),
                });
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations });
        }

        var url = $"{_options.BaseUrl}/models/{_options.Model}:generateContent";
        var json = await HttpJson.PostAsync(_http, url, body,
            req => req.Headers.Add("x-goog-api-key", _options.ApiKey), ProviderName, cancellationToken);

        return ParseResponse(json);
    }

    private static JsonArray BuildContents(IReadOnlyList<ChatMessage> messages)
    {
        var contents = new JsonArray();
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.User:
                    contents.Add(Content("user", new JsonObject { ["text"] = m.Text ?? "" }));
                    break;

                case ChatRole.Assistant:
                    // Повертаємо оригінальний вміст Gemini без змін — він може містити thoughtSignature,
                    // без якого нові моделі відхиляють продовження діалогу з викликами функцій.
                    if (m.ProviderName == ProviderName && m.ProviderData is JsonObject raw)
                    {
                        contents.Add(raw.DeepClone());
                        break;
                    }
                    var parts = new JsonArray();
                    if (!string.IsNullOrEmpty(m.Text)) parts.Add(new JsonObject { ["text"] = m.Text });
                    foreach (var call in m.ToolCalls)
                        parts.Add(new JsonObject
                        {
                            ["functionCall"] = new JsonObject { ["name"] = call.Name, ["args"] = call.Arguments.DeepClone() },
                        });
                    if (parts.Count == 0) parts.Add(new JsonObject { ["text"] = "..." });
                    contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts });
                    break;

                case ChatRole.Tool:
                    var responseParts = new JsonArray();
                    foreach (var r in m.ToolResults)
                    {
                        var fr = new JsonObject { ["name"] = r.Name, ["response"] = r.Content.DeepClone() };
                        if (!string.IsNullOrEmpty(r.CallId) && r.CallId.StartsWith("gem:", StringComparison.Ordinal))
                            fr["id"] = r.CallId[4..];
                        responseParts.Add(new JsonObject { ["functionResponse"] = fr });
                    }
                    contents.Add(new JsonObject { ["role"] = "user", ["parts"] = responseParts });
                    break;
            }
        }
        return contents;
    }

    private static JsonObject Content(string role, JsonObject part) =>
        new() { ["role"] = role, ["parts"] = new JsonArray(part) };

    private static LlmResponse ParseResponse(JsonNode json)
    {
        var candidate = json["candidates"]?[0];
        var content = candidate?["content"] as JsonObject;
        if (content is null)
        {
            var reason = json["promptFeedback"]?["blockReason"]?.ToString()
                         ?? candidate?["finishReason"]?.ToString() ?? "невідома причина";
            throw new LlmException($"[gemini] Модель не повернула відповідь ({reason}).");
        }

        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        foreach (var part in content["parts"]?.AsArray() ?? [])
        {
            if (part is null) continue;
            if (part["thought"]?.GetValue<bool>() == true) continue; // внутрішні "думки" моделі не показуємо

            if (part["text"] is JsonNode t) text.Append(t.GetValue<string>());

            if (part["functionCall"] is JsonObject fc)
            {
                var id = fc["id"]?.GetValue<string>();
                calls.Add(new ToolCall
                {
                    Id = id is null ? Guid.NewGuid().ToString("N")[..8] : "gem:" + id,
                    Name = fc["name"]?.GetValue<string>() ?? "",
                    Arguments = fc["args"] as JsonObject is { } args ? (JsonObject)args.DeepClone() : [],
                });
            }
        }

        if (!content.ContainsKey("role")) content["role"] = "model";

        var usage = json["usageMetadata"];
        return new LlmResponse
        {
            Text = text.Length == 0 ? null : text.ToString(),
            ToolCalls = calls,
            ProviderData = content.DeepClone(),
            Usage = usage is null ? null : new LlmUsage(
                usage["promptTokenCount"]?.GetValue<int>() ?? 0,
                usage["candidatesTokenCount"]?.GetValue<int>() ?? 0),
        };
    }
}
