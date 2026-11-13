using System.Text.Json.Nodes;
using AiDnD.Core.Game;
using AiDnD.Core.Llm;

namespace AiDnD.Core.Tools;

/// <summary>Контекст, доступний інструментам: стан світу та рушій правил.</summary>
public sealed class ToolContext(GameState state, RulesEngine rules)
{
    public GameState State { get; } = state;
    public RulesEngine Rules { get; } = rules;
}

/// <summary>
/// Базовий клас інструмента. Модель бачить лише Name/Description/Parameters,
/// а Execute виконує C#-код за правилами гри.
/// </summary>
public abstract class GameTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JsonObject Parameters { get; }

    public abstract JsonObject Execute(JsonObject args, ToolContext ctx);

    public ToolDefinition ToDefinition() => new(Name, Description, Parameters);

    // ---- Допоміжні методи для читання аргументів ----

    protected static string RequireString(JsonObject args, string name) =>
        OptionalString(args, name) is { Length: > 0 } s ? s : throw new GameRuleException($"Відсутній обов'язковий параметр '{name}'.");

    protected static string? OptionalString(JsonObject args, string name) =>
        args[name] is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToString()) : null;

    protected static int RequireInt(JsonObject args, string name) =>
        OptionalInt(args, name) ?? throw new GameRuleException($"Відсутній обов'язковий числовий параметр '{name}'.");

    protected static int? OptionalInt(JsonObject args, string name)
    {
        if (args[name] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)Math.Round(d);
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) return p;
        throw new GameRuleException($"Параметр '{name}' має бути цілим числом.");
    }
}

/// <summary>Мінімальний будівник JSON-схем (підмножина OpenAPI, яку розуміють Gemini та Ollama).</summary>
public static class Schema
{
    public static JsonObject String(string description, params string[] enumValues)
    {
        var s = new JsonObject { ["type"] = "string", ["description"] = description };
        if (enumValues.Length > 0) s["enum"] = new JsonArray(enumValues.Select(e => (JsonNode)e).ToArray());
        return s;
    }

    public static JsonObject Integer(string description) => new() { ["type"] = "integer", ["description"] = description };

    public static JsonObject Array(JsonObject items, string description) =>
        new() { ["type"] = "array", ["items"] = items, ["description"] = description };

    public static JsonObject Object(params (string Name, JsonObject Schema, bool Required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, isRequired) in properties)
        {
            props[name] = schema;
            if (isRequired) required.Add(name);
        }
        var obj = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Count > 0) obj["required"] = required;
        return obj;
    }
}
