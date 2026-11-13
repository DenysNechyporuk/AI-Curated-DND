using System.Text.Json.Nodes;
using AiDnD.Core.Game;
using AiDnD.Core.Llm;

namespace AiDnD.Core.Tools;

public sealed record ToolExecution(ToolCall Call, JsonObject Result, bool IsError);

/// <summary>Реєстр інструментів: знає їх схеми й безпечно виконує виклики моделі.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, GameTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public ToolRegistry Register(GameTool tool)
    {
        _tools[tool.Name] = tool;
        return this;
    }

    public static ToolRegistry CreateDefault() => new ToolRegistry()
        .Register(new RollDiceTool())
        .Register(new AttackTool())
        .Register(new SkillCheckTool())
        .Register(new SpawnNpcTool())
        .Register(new GiveItemTool())
        .Register(new UseItemTool())
        .Register(new MovePartyTool())
        .Register(new GetCharacterTool());

    public IReadOnlyList<ToolDefinition> Definitions => _tools.Values.Select(t => t.ToDefinition()).ToList();

    /// <summary>
    /// Виконує виклик. Помилки правил не "падають", а повертаються моделі як {"error": ...},
    /// щоб вона могла виправитися (напр. взяти правильний id).
    /// </summary>
    public ToolExecution Execute(ToolCall call, ToolContext ctx)
    {
        if (!_tools.TryGetValue(call.Name, out var tool))
            return Error(call, $"Невідомий інструмент '{call.Name}'. Доступні: {string.Join(", ", _tools.Keys)}.");
        try
        {
            return new ToolExecution(call, tool.Execute(call.Arguments, ctx), false);
        }
        catch (GameRuleException ex)
        {
            return Error(call, ex.Message);
        }
    }

    private static ToolExecution Error(ToolCall call, string message) =>
        new(call, new JsonObject { ["error"] = message }, true);
}
