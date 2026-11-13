using System.Text.Json.Nodes;

namespace AiDnD.Core.Llm;

public enum ChatRole { User, Assistant, Tool }

/// <summary>Запит моделі на виклик інструмента.</summary>
public sealed class ToolCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public JsonObject Arguments { get; set; } = [];
}

/// <summary>Результат виконання інструмента, що повертається моделі.</summary>
public sealed class ToolResult
{
    public string CallId { get; set; } = "";
    public string Name { get; set; } = "";
    public JsonObject Content { get; set; } = [];
}

/// <summary>Провайдер-незалежне повідомлення діалогу.</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; set; }
    public string? Text { get; set; }
    public List<ToolCall> ToolCalls { get; set; } = [];
    public List<ToolResult> ToolResults { get; set; } = [];

    /// <summary>
    /// "Сира" відповідь конкретного провайдера (напр. Gemini thoughtSignature), яку треба повернути без змін.
    /// Використовується лише тим провайдером, чия назва збігається з <see cref="ProviderName"/>.
    /// </summary>
    public JsonNode? ProviderData { get; set; }
    public string? ProviderName { get; set; }

    public static ChatMessage User(string text) => new() { Role = ChatRole.User, Text = text };

    public static ChatMessage Tool(IEnumerable<ToolResult> results) => new() { Role = ChatRole.Tool, ToolResults = results.ToList() };
}

/// <summary>Опис інструмента для моделі: назва, пояснення та JSON-схема параметрів.</summary>
public sealed record ToolDefinition(string Name, string Description, JsonObject Parameters);

public sealed class LlmRequest
{
    public required string SystemPrompt { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public IReadOnlyList<ToolDefinition> Tools { get; init; } = [];
    /// <summary>Якщо задано — модель має повернути JSON за цією схемою (структурований вивід).</summary>
    public JsonObject? ResponseSchema { get; init; }
    public double Temperature { get; init; } = 0.8;
}

public sealed record LlmUsage(int InputTokens, int OutputTokens);

public sealed class LlmResponse
{
    public string? Text { get; init; }
    public List<ToolCall> ToolCalls { get; init; } = [];
    public JsonNode? ProviderData { get; init; }
    public LlmUsage? Usage { get; init; }
}

/// <summary>Помилка зв'язку з моделлю (мережа, ключ, ліміти).</summary>
public sealed class LlmException(string message, Exception? inner = null) : Exception(message, inner);
