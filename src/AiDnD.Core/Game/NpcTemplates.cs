namespace AiDnD.Core.Game;

/// <summary>Шаблони типових істот. Статистики задає код, а не модель.</summary>
public static class NpcTemplates
{
    private static readonly Dictionary<string, Func<Character>> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["goblin"] = () => Make("Гоблін", "goblin", 7, 15, new() { Str = 8, Dex = 14, Con = 10, Int = 10, Wis = 8, Cha = 8 },
            new Weapon { Name = "Ятаган", DamageDice = "1d6", Ability = Ability.Dex }, ["stealth"]),
        ["wolf"] = () => Make("Вовк", "wolf", 11, 13, new() { Str = 12, Dex = 15, Con = 12, Int = 3, Wis = 12, Cha = 6 },
            new Weapon { Name = "Укус", DamageDice = "2d4", Ability = Ability.Str }, ["perception", "stealth"]),
        ["bandit"] = () => Make("Бандит", "bandit", 11, 12, new() { Str = 11, Dex = 12, Con = 12, Int = 10, Wis = 10, Cha = 10 },
            new Weapon { Name = "Шабля", DamageDice = "1d6", Ability = Ability.Dex }, []),
        ["skeleton"] = () => Make("Скелет", "skeleton", 13, 13, new() { Str = 10, Dex = 14, Con = 15, Int = 6, Wis = 8, Cha = 5 },
            new Weapon { Name = "Короткий меч", DamageDice = "1d6", Ability = Ability.Dex }, []),
        ["orc"] = () => Make("Орк", "orc", 15, 13, new() { Str = 16, Dex = 12, Con = 16, Int = 7, Wis = 11, Cha = 10 },
            new Weapon { Name = "Сокира", DamageDice = "1d12", Ability = Ability.Str }, ["intimidation"]),
        ["villager"] = () => Make("Селянин", "villager", 4, 10, new(),
            new Weapon { Name = "Вила", DamageDice = "1d4", Ability = Ability.Str }, []),
        ["cultist"] = () => Make("Культист", "cultist", 9, 12, new() { Str = 11, Dex = 12, Con = 10, Int = 10, Wis = 11, Cha = 10 },
            new Weapon { Name = "Ритуальний кинджал", DamageDice = "1d4", Ability = Ability.Dex }, ["deception", "religion"]),
    };

    public static IEnumerable<string> Types => Templates.Keys;

    public static Character Create(string type)
    {
        if (!Templates.TryGetValue(type, out var factory))
            throw new GameRuleException($"Невідомий тип істоти '{type}'. Доступні: {string.Join(", ", Types)}.");
        return factory();
    }

    private static Character Make(string name, string type, int hp, int ac, AbilityScores abilities, Weapon weapon, List<string> skills) => new()
    {
        Name = name,
        CharacterClass = type,
        MaxHp = hp,
        Hp = hp,
        ArmorClass = ac,
        Abilities = abilities,
        Weapons = [weapon],
        SkillProficiencies = skills,
        Kind = CharacterKind.Npc,
    };

    /// <summary>Стартовий персонаж гравця (воїн 1 рівня).</summary>
    public static Character CreateDefaultHero(string name) => new()
    {
        Id = "player_1",
        Name = name,
        Kind = CharacterKind.Player,
        Disposition = Disposition.Friendly,
        CharacterClass = "Воїн",
        MaxHp = 13,
        Hp = 13,
        ArmorClass = 16,
        Abilities = new() { Str = 16, Dex = 12, Con = 14, Int = 10, Wis = 11, Cha = 9 },
        SkillProficiencies = ["athletics", "perception", "intimidation"],
        Weapons =
        [
            new Weapon { Name = "Довгий меч", DamageDice = "1d8", Ability = Ability.Str },
            new Weapon { Name = "Кинджал", DamageDice = "1d4", Ability = Ability.Dex },
        ],
        Inventory =
        [
            new InventoryItem { Name = "Смолоскип", Quantity = 2 },
            new InventoryItem { Name = "Зілля лікування", Quantity = 1, Description = "Відновлює 2d4+2 HP" },
            new InventoryItem { Name = "Золото", Quantity = 15 },
        ],
    };

    public static GameState CreateNewCampaign(string heroName)
    {
        var state = new GameState
        {
            CampaignName = "Попіл Чорнолісу",
            CurrentLocation = new Location
            {
                Name = "Таверна «Згаслий ліхтар»",
                Description = "Напівпорожня таверна на краю Чорнолісу. Дощ, запах вологого дерева, шепіт про зниклих дітей.",
            },
        };
        state.Characters.Add(CreateDefaultHero(heroName));

        var innkeeper = Create("villager");
        innkeeper.Id = state.NextId("innkeeper");
        innkeeper.Name = "Мирослава, корчмарка";
        innkeeper.Disposition = Disposition.Friendly;
        innkeeper.Motivation = "Хоче знайти свого сина, що зник у лісі три ночі тому";
        state.Characters.Add(innkeeper);
        return state;
    }
}
