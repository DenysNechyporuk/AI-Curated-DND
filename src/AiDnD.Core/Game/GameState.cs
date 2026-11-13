using System.Text;
using AiDnD.Core.Llm;

namespace AiDnD.Core.Game;

public sealed class Location
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Єдине джерело правди про світ: персонажі, HP, локація, пам'ять кампанії.
/// LLM бачить лише його знімок (<see cref="ToPromptSnapshot"/>) і не може змінити напряму.
/// </summary>
public sealed class GameState
{
    public string CampaignName { get; set; } = "Нова кампанія";
    public int Turn { get; set; }
    public Location CurrentLocation { get; set; } = new();
    public List<Character> Characters { get; set; } = [];

    /// <summary>Стислі підсумки попередніх сцен (довготривала пам'ять).</summary>
    public List<string> SceneSummaries { get; set; } = [];

    /// <summary>Журнал механічних подій (кидки, урон) — для UI і налагодження.</summary>
    public List<string> EventLog { get; set; } = [];

    /// <summary>Недавня історія діалогу з моделлю (короткострокова пам'ять).</summary>
    public List<ChatMessage> History { get; set; } = [];

    public Dictionary<string, int> IdCounters { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<Character> Party => Characters.Where(c => c.Kind == CharacterKind.Player);

    public Character? Find(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        return Characters.FirstOrDefault(c => c.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
            ?? Characters.FirstOrDefault(c => c.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public Character Require(string? idOrName) =>
        Find(idOrName) ?? throw new GameRuleException(
            $"Персонажа '{idOrName}' не існує. Наявні id: {string.Join(", ", Characters.Select(c => c.Id))}.");

    public string NextId(string prefix)
    {
        var key = Slug(prefix);
        IdCounters[key] = IdCounters.GetValueOrDefault(key) + 1;
        return $"{key}_{IdCounters[key]}";
    }

    public void Log(string message) => EventLog.Add($"[хід {Turn}] {message}");

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        var result = sb.ToString().Trim('_');
        return result.Length == 0 ? "npc" : result;
    }

    /// <summary>Компактний текстовий знімок стану, який підставляється в системний промпт на кожному запиті.</summary>
    public string ToPromptSnapshot()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Локація: {CurrentLocation.Name} — {CurrentLocation.Description}");
        sb.AppendLine($"Хід: {Turn}");
        sb.AppendLine("Персонажі (id | ім'я | тип | ставлення | HP | AC | зброя):");
        foreach (var c in Characters)
        {
            var status = c.IsAlive ? $"{c.Hp}/{c.MaxHp}" : "0 (повалений)";
            var weapons = c.Weapons.Count == 0 ? "кулаки" : string.Join(", ", c.Weapons.Select(w => $"{w.Name} {w.DamageDice}"));
            sb.AppendLine($"- {c.Id} | {c.Name} | {(c.Kind == CharacterKind.Player ? "ГРАВЕЦЬ" : "NPC")} {c.CharacterClass} | {c.Disposition} | HP {status} | AC {c.ArmorClass} | {weapons}");
            if (c.Kind == CharacterKind.Player && c.Inventory.Count > 0)
                sb.AppendLine($"    інвентар: {string.Join(", ", c.Inventory.Select(i => $"{i.Name} x{i.Quantity}"))}");
        }
        return sb.ToString();
    }
}
