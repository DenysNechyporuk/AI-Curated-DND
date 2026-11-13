namespace AiDnD.Core.Game;

public enum Ability { Str, Dex, Con, Int, Wis, Cha }

public enum CharacterKind { Player, Npc }

public enum Disposition { Friendly, Neutral, Hostile }

public sealed class AbilityScores
{
    public int Str { get; set; } = 10;
    public int Dex { get; set; } = 10;
    public int Con { get; set; } = 10;
    public int Int { get; set; } = 10;
    public int Wis { get; set; } = 10;
    public int Cha { get; set; } = 10;

    public int Get(Ability ability) => ability switch
    {
        Ability.Str => Str,
        Ability.Dex => Dex,
        Ability.Con => Con,
        Ability.Int => Int,
        Ability.Wis => Wis,
        Ability.Cha => Cha,
        _ => throw new ArgumentOutOfRangeException(nameof(ability))
    };

    public static int ModifierOf(int score) => (int)Math.Floor((score - 10) / 2.0);
}

public sealed class Weapon
{
    public string Name { get; set; } = "Кулак";
    /// <summary>Кубики урону, напр. "1d8".</summary>
    public string DamageDice { get; set; } = "1d2";
    /// <summary>Характеристика для атаки та урону (Str для мечів, Dex для луків/кинджалів).</summary>
    public Ability Ability { get; set; } = Ability.Str;

    public static Weapon Unarmed => new() { Name = "Кулак", DamageDice = "1d2", Ability = Ability.Str };
}

public sealed class InventoryItem
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string? Description { get; set; }
}

public sealed class Character
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public CharacterKind Kind { get; set; } = CharacterKind.Npc;
    public Disposition Disposition { get; set; } = Disposition.Neutral;
    public string CharacterClass { get; set; } = "";
    public string? Description { get; set; }
    public string? Motivation { get; set; }

    public int Level { get; set; } = 1;
    public int MaxHp { get; set; } = 10;
    public int Hp { get; set; } = 10;
    public int ArmorClass { get; set; } = 10;

    public AbilityScores Abilities { get; set; } = new();
    /// <summary>Навички, якими персонаж володіє (додається бонус майстерності).</summary>
    public List<string> SkillProficiencies { get; set; } = [];
    public List<Weapon> Weapons { get; set; } = [];
    public List<InventoryItem> Inventory { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAlive => Hp > 0;
    [System.Text.Json.Serialization.JsonIgnore]
    public int ProficiencyBonus => 2 + (Level - 1) / 4;
    public int Modifier(Ability ability) => AbilityScores.ModifierOf(Abilities.Get(ability));

    public void TakeDamage(int amount) => Hp = Math.Max(0, Hp - Math.Max(0, amount));
    public void Heal(int amount) => Hp = Math.Min(MaxHp, Hp + Math.Max(0, amount));

    public Weapon FindWeapon(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Weapons.FirstOrDefault() ?? Weapon.Unarmed;
        return Weapons.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Weapons.FirstOrDefault(w => w.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                                        || name.Contains(w.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new GameRuleException(
                $"У {Name} немає зброї '{name}'. Доступно: {(Weapons.Count == 0 ? "лише кулаки" : string.Join(", ", Weapons.Select(w => w.Name)))}.");
    }

    public void AddItem(string name, int quantity, string? description = null)
    {
        var existing = Inventory.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) existing.Quantity += quantity;
        else Inventory.Add(new InventoryItem { Name = name, Quantity = quantity, Description = description });
    }
}
