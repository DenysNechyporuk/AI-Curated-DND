using System.Text.Json.Nodes;

namespace AiDnD.Core.Llm.Providers;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    /// <summary>Модель з підтримкою tools, напр. qwen3:8b, llama3.1:8b, mistral-nemo.</summary>
    public string Model { get; set; } = "qwen3:8b";
}

/// <summary>
/// Локальна модель через Ollama (/api/chat). Працює офлайн, безкоштовно.
/// Документація: https://github.com/ollama/ollama/blob/main/docs/api.md
/// </summary>
public sealed class OllamaProvider(OllamaOptions options, HttpClient? http = null) : ILlmProvider
{
    public const string ProviderName = "ollama";

    // Локальні моделі на CPU можуть думати довго
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

    public string Name => ProviderName;
    public string Model => options.Model;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt });
        foreach (var m in request.Messages)
        {
            switch (m.Role)
            {
                case ChatRole.User:
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = m.Text ?? "" });
                    break;
                case ChatRole.Assistant:
                    var msg = new JsonObject { ["role"] = "assistant", ["content"] = m.Text ?? "" };
                    if (m.ToolCalls.Count > 0)
                    {
                        var calls = new JsonArray();
                        foreach (var c in m.ToolCalls)
                            calls.Add(new JsonObject
                            {
                                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments.DeepClone() },
                            });
                        msg["tool_calls"] = calls;
                    }
                    messages.Add(msg);
                    break;
                case ChatRole.Tool:
                    foreach (var r in m.ToolResults)
                        messages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_name"] = r.Name,
                            ["content"] = r.Content.ToJsonString(),
                        });
                    break;
            }
        }

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["options"] = new JsonObject { ["temperature"] = request.Temperature },
        };

        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var t in request.Tools)
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.Parameters.DeepClone(),
                    },
                });
            body["tools"] = tools;
        }

        if (request.ResponseSchema is not null)
            body["format"] = request.ResponseSchema.DeepClone();

        var json = await HttpJson.PostAsync(_http, $"{options.BaseUrl.TrimEnd('/')}/api/chat", body, null, ProviderName, cancellationToken);

        var message = json["message"] ?? throw new LlmException("[ollama] Відповідь без поля message.");
        var toolCalls = new List<ToolCall>();
        foreach (var tc in message["tool_calls"]?.AsArray() ?? [])
        {
            var fn = tc?["function"];
            if (fn is null) continue;
            var args = fn["arguments"] switch
            {
                JsonObject o => (JsonObject)o.DeepClone(),
                JsonValue v when v.TryGetValue<string>(out var s) => JsonNode.Parse(s) as JsonObject ?? [],
                _ => [],
            };
            toolCalls.Add(new ToolCall { Name = fn["name"]?.GetValue<string>() ?? "", Arguments = args });
        }

        var text = message["content"]?.GetValue<string>();
        return new LlmResponse
        {
            Text = string.IsNullOrWhiteSpace(text) ? null : StripThinking(text),
            ToolCalls = toolCalls,
            Usage = new LlmUsage(json["prompt_eval_count"]?.GetValue<int>() ?? 0, json["eval_count"]?.GetValue<int>() ?? 0),
        };
    }

    /// <summary>Деякі моделі (qwen3, deepseek-r1) пишуть міркування в &lt;think&gt;...&lt;/think&gt; — прибираємо.</summary>
    private static string StripThinking(string text)
    {
        var end = text.IndexOf("</think>", StringComparison.Ordinal);
        return end >= 0 ? text[(end + "</think>".Length)..].Trim() : text.Trim();
    }
}
