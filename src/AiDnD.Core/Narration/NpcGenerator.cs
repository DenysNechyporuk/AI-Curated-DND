using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AiDnD.Core.Game;
using AiDnD.Core.Llm;
using AiDnD.Core.Tools;

namespace AiDnD.Core.Narration;

/// <summary>DTO, у який парситься JSON від моделі.</summary>
public sealed class NpcBlueprint
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("character_class")] public string CharacterClass { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("motivation")] public string Motivation { get; set; } = "";
    [JsonPropertyName("disposition")] public string Disposition { get; set; } = "neutral";
    [JsonPropertyName("hp")] public int Hp { get; set; }
    [JsonPropertyName("armor_class")] public int ArmorClass { get; set; }
    [JsonPropertyName("str")] public int Str { get; set; }
    [JsonPropertyName("dex")] public int Dex { get; set; }
    [JsonPropertyName("con")] public int Con { get; set; }
    [JsonPropertyName("int")] public int Int { get; set; }
    [JsonPropertyName("wis")] public int Wis { get; set; }
    [JsonPropertyName("cha")] public int Cha { get; set; }
    [JsonPropertyName("weapon_name")] public string WeaponName { get; set; } = "";
    [JsonPropertyName("weapon_damage")] public string WeaponDamage { get; set; } = "1d6";
}

/// <summary>
/// Структурований вивід: модель повертає JSON за схемою → C#-об'єкт → валідація кодом → реальні дані гри.
/// </summary>
public sealed class NpcGenerator(ILlmProvider llm, DiceRoller dice)
{
    public static JsonObject ResponseSchema => Schema.Object(
        ("name", Schema.String("Ім'я"), true),
        ("character_class", Schema.String("Клас або професія"), true),
        ("description", Schema.String("Зовнішність, 1-2 речення"), true),
        ("motivation", Schema.String("Мотивація або таємниця"), true),
        ("disposition", Schema.String("Ставлення до героїв", "friendly", "neutral", "hostile"), true),
        ("hp", Schema.Integer("Хіти 1..60"), true),
        ("armor_class", Schema.Integer("Клас броні 8..20"), true),
        ("str", Schema.Integer("Сила 3..18"), true),
        ("dex", Schema.Integer("Спритність 3..18"), true),
        ("con", Schema.Integer("Статура 3..18"), true),
        ("int", Schema.Integer("Інтелект 3..18"), true),
        ("wis", Schema.Integer("Мудрість 3..18"), true),
        ("cha", Schema.Integer("Харизма 3..18"), true),
        ("weapon_name", Schema.String("Назва зброї"), true),
        ("weapon_damage", Schema.String("Кубики урону зброї, напр. 1d6"), true));

    public async Task<Character> GenerateAsync(string hint, GameState state, CancellationToken ct = default)
    {
        var response = await llm.CompleteAsync(new LlmRequest
        {
            SystemPrompt = Prompts.NpcGenerator,
            Messages = [ChatMessage.User($"Локація: {state.CurrentLocation.Name}. Запит: {hint}")],
            ResponseSchema = ResponseSchema,
            Temperature = 1.0,
        }, ct);

        var json = response.Text ?? throw new LlmException("Модель не повернула JSON.");
        NpcBlueprint blueprint;
        try
        {
            blueprint = JsonSerializer.Deserialize<NpcBlueprint>(ExtractJson(json))
                        ?? throw new LlmException("Порожній JSON.");
        }
        catch (JsonException ex)
        {
            throw new LlmException($"Модель повернула некоректний JSON: {ex.Message}", ex);
        }

        var npc = ToCharacter(blueprint);
        npc.Id = state.NextId("npc");
        state.Characters.Add(npc);
        state.Log($"Згенеровано NPC {npc.Name} ({npc.Id})");
        return npc;
    }

    /// <summary>Модель може "переборщити" — код обрізає значення до допустимих меж.</summary>
    public Character ToCharacter(NpcBlueprint b)
    {
        int Clamp(int v) => Math.Clamp(v, 3, 18);
        var damage = b.WeaponDamage;
        try { dice.Roll(damage); } catch (GameRuleException) { damage = "1d6"; }

        var hp = Math.Clamp(b.Hp, 1, 60);
        return new Character
        {
            Name = string.IsNullOrWhiteSpace(b.Name) ? "Незнайомець" : b.Name.Trim(),
            CharacterClass = b.CharacterClass,
            Description = b.Description,
            Motivation = b.Motivation,
            Kind = CharacterKind.Npc,
            Disposition = Enum.TryParse<Disposition>(b.Disposition, true, out var d) ? d : Disposition.Neutral,
            MaxHp = hp,
            Hp = hp,
            ArmorClass = Math.Clamp(b.ArmorClass, 8, 20),
            Abilities = new AbilityScores
            {
                Str = Clamp(b.Str), Dex = Clamp(b.Dex), Con = Clamp(b.Con),
                Int = Clamp(b.Int), Wis = Clamp(b.Wis), Cha = Clamp(b.Cha),
            },
            Weapons = [new Weapon
            {
                Name = string.IsNullOrWhiteSpace(b.WeaponName) ? "Ніж" : b.WeaponName,
                DamageDice = damage,
                Ability = b.Dex > b.Str ? Ability.Dex : Ability.Str,
            }],
        };
    }

    /// <summary>Деякі моделі обгортають JSON у ```json ... ``` — прибираємо.</summary>
    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('{'), end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
