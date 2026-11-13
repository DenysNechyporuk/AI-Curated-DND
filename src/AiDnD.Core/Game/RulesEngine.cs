namespace AiDnD.Core.Game;

public sealed record AttackResult(
    string AttackerId,
    string TargetId,
    string Weapon,
    int D20,
    int AttackBonus,
    int AttackTotal,
    int TargetArmorClass,
    bool Hit,
    bool Critical,
    bool Fumble,
    int Damage,
    string? DamageRoll,
    int TargetHp,
    int TargetMaxHp,
    bool TargetKilled);

public sealed record SkillCheckResult(
    string CharacterId,
    string Skill,
    Ability Ability,
    int D20,
    int Bonus,
    int Total,
    int Difficulty,
    bool Success,
    bool NaturalTwenty,
    bool NaturalOne);

/// <summary>
/// Механіка гри (спрощена D&amp;D 5e). Тут — і тільки тут — визначається успіх дій.
/// LLM лише просить виконати дію через інструмент.
/// </summary>
public sealed class RulesEngine(DiceRoller dice)
{
    public DiceRoller Dice { get; } = dice;

    public static readonly IReadOnlyDictionary<string, Ability> SkillAbilities =
        new Dictionary<string, Ability>(StringComparer.OrdinalIgnoreCase)
        {
            ["athletics"] = Ability.Str,
            ["acrobatics"] = Ability.Dex,
            ["stealth"] = Ability.Dex,
            ["sleight_of_hand"] = Ability.Dex,
            ["arcana"] = Ability.Int,
            ["history"] = Ability.Int,
            ["investigation"] = Ability.Int,
            ["religion"] = Ability.Int,
            ["nature"] = Ability.Int,
            ["perception"] = Ability.Wis,
            ["insight"] = Ability.Wis,
            ["survival"] = Ability.Wis,
            ["medicine"] = Ability.Wis,
            ["animal_handling"] = Ability.Wis,
            ["persuasion"] = Ability.Cha,
            ["deception"] = Ability.Cha,
            ["intimidation"] = Ability.Cha,
            ["performance"] = Ability.Cha,
            // "Чисті" перевірки характеристик
            ["strength"] = Ability.Str,
            ["dexterity"] = Ability.Dex,
            ["constitution"] = Ability.Con,
            ["intelligence"] = Ability.Int,
            ["wisdom"] = Ability.Wis,
            ["charisma"] = Ability.Cha,
        };

    public AttackResult Attack(Character attacker, Character target, string? weaponName)
    {
        if (!attacker.IsAlive) throw new GameRuleException($"{attacker.Name} непритомний/мертвий і не може атакувати.");
        if (!target.IsAlive) throw new GameRuleException($"{target.Name} вже повалений (0 HP).");
        if (attacker.Id == target.Id) throw new GameRuleException("Персонаж не може атакувати сам себе.");

        var weapon = attacker.FindWeapon(weaponName);
        int abilityMod = attacker.Modifier(weapon.Ability);
        int attackBonus = abilityMod + attacker.ProficiencyBonus;

        int d20 = Dice.RollD20();
        bool critical = d20 == 20;
        bool fumble = d20 == 1;
        int attackTotal = d20 + attackBonus;
        bool hit = critical || (!fumble && attackTotal >= target.ArmorClass);

        int damage = 0;
        string? damageRoll = null;
        if (hit)
        {
            var roll = Dice.Roll(weapon.DamageDice, diceMultiplier: critical ? 2 : 1);
            damage = Math.Max(1, roll.Total + abilityMod);
            damageRoll = $"{roll} {(abilityMod >= 0 ? "+" : "")}{abilityMod} (мод.) = {damage}";
            target.TakeDamage(damage);
        }

        return new AttackResult(attacker.Id, target.Id, weapon.Name, d20, attackBonus, attackTotal,
            target.ArmorClass, hit, critical, fumble, damage, damageRoll, target.Hp, target.MaxHp, !target.IsAlive);
    }

    public SkillCheckResult SkillCheck(Character character, string skill, int difficulty)
    {
        if (!SkillAbilities.TryGetValue(skill, out var ability))
            throw new GameRuleException($"Невідома навичка '{skill}'. Доступні: {string.Join(", ", SkillAbilities.Keys)}.");
        if (difficulty is < 1 or > 40) throw new GameRuleException("Складність (DC) має бути в межах 1..40.");

        int bonus = character.Modifier(ability);
        if (character.SkillProficiencies.Contains(skill, StringComparer.OrdinalIgnoreCase))
            bonus += character.ProficiencyBonus;

        int d20 = Dice.RollD20();
        int total = d20 + bonus;
        return new SkillCheckResult(character.Id, skill.ToLowerInvariant(), ability, d20, bonus, total, difficulty,
            total >= difficulty, d20 == 20, d20 == 1);
    }
}
