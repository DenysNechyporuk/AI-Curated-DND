using System.Text.Json.Nodes;
using AiDnD.Core.Game;

namespace AiDnD.Core.Tools;

public sealed class RollDiceTool : GameTool
{
    public override string Name => "roll_dice";
    public override string Description =>
        "Кинути кубики за виразом (напр. '1d20', '2d6+3'). Для довільних випадкових подій, що не є атакою чи перевіркою навички.";
    public override JsonObject Parameters => Schema.Object(
        ("expression", Schema.String("Вираз кубиків у нотації NdM+K"), true),
        ("reason", Schema.String("Для чого кидок (коротко)"), false));

    public override JsonObject Execute(JsonObject args, ToolContext ctx)
    {
        var roll = ctx.Rules.Dice.Roll(RequireString(args, "expression"));
        ctx.State.Log($"Кидок {roll} ({OptionalString(args, "reason") ?? "—"})");
        return new JsonObject
        {
            ["expression"] = roll.Expression,
            ["rolls"] = new JsonArray(roll.Rolls.Select(r => (JsonNode)r.Value).ToArray()),
            ["modifier"] = roll.Modifier,
            ["total"] = roll.Total,
        };
    }
}

public sealed class AttackTool : GameTool
{
    public override string Name => "attack";
    public override string Description =>
        "Виконати атаку зброєю. Код сам кидає d20 проти AC цілі, рахує урон і оновлює HP. " +
        "Використовуй для БУДЬ-якої атаки — і гравця, і NPC.";
    public override JsonObject Parameters => Schema.Object(
        ("attacker_id", Schema.String("id атакуючого з поточного стану"), true),
        ("target_id", Schema.String("id цілі з поточного стану"), true),
        ("weapon", Schema.String("Назва зброї атакуючого; якщо не вказано — основна зброя"), false));

    public override JsonObject Execute(JsonObject args, ToolContext ctx)
    {
        var attacker = ctx.State.Require(RequireString(args, "attacker_id"));
        var target = ctx.State.Require(RequireString(args, "target_id"));
        var r = ctx.Rules.Attack(attacker, target, OptionalString(args, "weapon"));

        // Атакований NPC стає ворожим
        if (target.Kind == CharacterKind.Npc && attacker.Kind == CharacterKind.Player)
            target.Disposition = Disposition.Hostile;

        ctx.State.Log($"{attacker.Name} атакує {target.Name} ({r.Weapon}): d20={r.D20}+{r.AttackBonus}={r.AttackTotal} vs AC {r.TargetArmorClass} → " +
                      (r.Hit ? $"{(r.Critical ? "КРИТ! " : "")}влучання, {r.Damage} урону, HP {r.TargetHp}/{r.TargetMaxHp}{(r.TargetKilled ? " — ПОВАЛЕНИЙ" : "")}" : "промах"));

        return new JsonObject
        {
            ["attacker"] = attacker.Name,
            ["target"] = target.Name,
            ["weapon"] = r.Weapon,
            ["d20"] = r.D20,
            ["attack_total"] = r.AttackTotal,
            ["target_ac"] = r.TargetArmorClass,
            ["hit"] = r.Hit,
            ["critical"] = r.Critical,
            ["fumble"] = r.Fumble,
            ["damage"] = r.Damage,
            ["target_hp"] = r.TargetHp,
            ["target_max_hp"] = r.TargetMaxHp,
            ["target_down"] = r.TargetKilled,
        };
    }
}

public sealed class SkillCheckTool : GameTool
{
    public override string Name => "skill_check";
    public override string Description =>
        "Перевірка навички (d20 + модифікатор проти складності DC). Використовуй, коли результат дії не очевидний: " +
        "скритність, переконання, сприйняття, атлетика тощо. DC: 5 легко, 10 середньо, 15 складно, 20 дуже складно, 25 майже неможливо.";
    public override JsonObject Parameters => Schema.Object(
        ("character_id", Schema.String("id персонажа, що виконує перевірку"), true),
        ("skill", Schema.String("Навичка", RulesEngine.SkillAbilities.Keys.ToArray()), true),
        ("difficulty", Schema.Integer("Складність DC (5-30)"), true));

    public override JsonObject Execute(JsonObject args, ToolContext ctx)
    {
        var character = ctx.State.Require(RequireString(args, "character_id"));
        var r = ctx.Rules.SkillCheck(character, RequireString(args, "skill"), RequireInt(args, "difficulty"));
        ctx.State.Log($"{character.Name}: перевірка {r.Skill} d20={r.D20}{(r.Bonus >= 0 ? "+" : "")}{r.Bonus}={r.Total} vs DC {r.Difficulty} → {(r.Success ? "успіх" : "провал")}");
        return new JsonObject
        {
            ["character"] = character.Name,
            ["skill"] = r.Skill,
            ["d20"] = r.D20,
            ["bonus"] = r.Bonus,
            ["total"] = r.Total,
            ["difficulty"] = r.Difficulty,
            ["success"] = r.Success,
            ["natural_20"] = r.NaturalTwenty,
            ["natural_1"] = r.NaturalOne,
        };
    }
}

public sealed class SpawnNpcTool : GameTool
{
    public override string Name => "spawn_npc";
    public override string Description =>
        "Додати в сцену нову істоту чи NPC за шаблоном. Статистики бере код. Викликай ПЕРЕД тим, як NPC з'явиться в описі, " +
        "щоб у нього був id. Повертає id нового персонажа.";
    public override JsonObject Parameters => Schema.Object(
        ("type", Schema.String("Тип істоти", NpcTemplates.Types.ToArray()), true),
        ("name", Schema.String("Ім'я або опис (напр. 'Гоблін-лучник')"), false),
        ("disposition", Schema.String("Ставлення до героїв", "friendly", "neutral", "hostile"), false),
        ("count", Schema.Integer("Кількість однакових істот (1-6)"), false));

    public override JsonObject Execute(JsonObject args, ToolContext ctx)
    {
        var type = RequireString(args, "type");
        var count = Math.Clamp(OptionalInt(args, "count") ?? 1, 1, 6);
        var disposition = Enum.TryParse<Disposition>(OptionalString(args, "disposition"), true, out var d) ? d : Disposition.Hostile;
        var name = OptionalString(args, "name");

        var spawned = new JsonArray();
        for (int i = 0; i < count; i++)
        {
            var npc = NpcTemplates.Create(type);
            npc.Id = ctx.State.NextId(type);
            if (!string.IsNullOrWhiteSpace(name)) npc.Name = count > 1 ? $"{name} {i + 1}" : name;
            else if (count > 1) npc.Name = $"{npc.Name} {i + 1}";
            npc.Disposition = disposition;
            ctx.State.Characters.Add(npc);
            ctx.State.Log($"З'являється {npc.Name} ({npc.Id}), HP {npc.Hp}, AC {npc.ArmorClass}");
            spawned.Add(new JsonObject { ["id"] = npc.Id, ["name"] = npc.Name, ["hp"] = npc.Hp, ["ac"] = npc.ArmorClass });
        }
        return new JsonObject { ["spawned"] = spawned };
    }
}

public sealed class GiveItemTool : GameTool
{
    public override string Name => "give_item";
    public override string Description => "Додати предмет в інвентар персонажа (знахідка, нагорода, покупка).";
    public override JsonObject Parameters => Schema.Object(
        ("character_id", Schema.String("id отримувача"), true),
        ("item", Schema.String("Назва предмета"), true),
        ("quantity", Schema.Integer("Кількість (за замовчуванням 1)"), false),
        ("description", Schema.String("Короткий опис предмета"), false));

    public override JsonObject Execute(JsonObject args, ToolContext ctx)
    {
        var c = ctx.State.Require(RequireString(args, "character_id"));
        var item = RequireString(args, "item");
        var qty = Math.Clamp(OptionalInt(args, "quantity") ?? 1, 1, 999);
        c.AddItem(item, qty, OptionalString(args, "description"));
        ctx.State.Log($"{c.Name} отримує {item} x{qty}");
        return new JsonObject { ["character"] = c.Name, ["item"] = item, ["quantity"] = qty, ["ok"] = true };
    }
}

public sealed class UseItemTool : GameTool
{
    public override string Name => "use_item";
    public override string Description =>
        "Використати/витратити предмет з інвентаря. 'Зілля лікування' відновлює 2d4+2 HP (рахує код).";
    public override JsonObject Parameters => Schema.Object(
        ("character_id", Schema.String("id персонажа, що використовує предмет"), true),
        ("item", Schema.String("Назва предмета з інвентаря"), true),
        ("target_id", Schema.String("id цілі (якщо інша, ніж сам персонаж)"), false));

    public override JsonObject Execute(JsonObject args, ToolContext ctx)
    {
        var c = ctx.State.Require(RequireString(args, "character_id"));
        var name = RequireString(args, "item");
        var item = c.Inventory.FirstOrDefault(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                   ?? throw new GameRuleException($"У {c.Name} немає предмета '{name}'.");
        var target = OptionalString(args, "target_id") is { } tid ? ctx.State.Require(tid) : c;

        item.Quantity--;
        if (item.Quantity <= 0) c.Inventory.Remove(item);

        var result = new JsonObject { ["item"] = item.Name, ["user"] = c.Name, ["target"] = target.Name };
        if (item.Name.Contains("лікування", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("healing", StringComparison.OrdinalIgnoreCase))
        {
            var roll = ctx.Rules.Dice.Roll("2d4+2");
            target.Heal(roll.Total);
            result["healed"] = roll.Total;
            result["target_hp"] = target.Hp;
            result["target_max_hp"] = target.MaxHp;
        }
        ctx.State.Log($"{c.Name} використовує {item.Name} на {target.Name}");
        return result;
    }
}

public sealed class MovePartyTool : GameTool
{
    public override string Name => "move_party";
    public override string Description =>
        "Перемістити групу в нову локацію. Повалені вороги й NPC старої локації залишаються позаду.";
    public override JsonObject Parameters => Schema.Object(
        ("location_name", Schema.String("Назва нової локації"), true),
        ("description", Schema.String("Короткий опис локації (1-2 речення)"), true));

    public override JsonObject Execute(JsonObject args, ToolContext ctx)
    {
        var from = ctx.State.CurrentLocation.Name;
        ctx.State.CurrentLocation = new Location
        {
            Name = RequireString(args, "location_name"),
            Description = OptionalString(args, "description") ?? "",
        };
        ctx.State.Characters.RemoveAll(c => c.Kind == CharacterKind.Npc);
        ctx.State.Log($"Група переходить: {from} → {ctx.State.CurrentLocation.Name}");
        return new JsonObject { ["from"] = from, ["to"] = ctx.State.CurrentLocation.Name, ["ok"] = true };
    }
}

public sealed class GetCharacterTool : GameTool
{
    public override string Name => "get_character";
    public override string Description => "Отримати повний лист персонажа: характеристики, навички, інвентар.";
    public override JsonObject Parameters => Schema.Object(("character_id", Schema.String("id персонажа"), true));

    public override JsonObject Execute(JsonObject args, ToolContext ctx)
    {
        var c = ctx.State.Require(RequireString(args, "character_id"));
        return new JsonObject
        {
            ["id"] = c.Id,
            ["name"] = c.Name,
            ["class"] = c.CharacterClass,
            ["level"] = c.Level,
            ["hp"] = c.Hp,
            ["max_hp"] = c.MaxHp,
            ["ac"] = c.ArmorClass,
            ["abilities"] = new JsonObject
            {
                ["str"] = c.Abilities.Str, ["dex"] = c.Abilities.Dex, ["con"] = c.Abilities.Con,
                ["int"] = c.Abilities.Int, ["wis"] = c.Abilities.Wis, ["cha"] = c.Abilities.Cha,
            },
            ["skills"] = new JsonArray(c.SkillProficiencies.Select(s => (JsonNode)s).ToArray()),
            ["weapons"] = new JsonArray(c.Weapons.Select(w => (JsonNode)$"{w.Name} ({w.DamageDice})").ToArray()),
            ["inventory"] = new JsonArray(c.Inventory.Select(i => (JsonNode)$"{i.Name} x{i.Quantity}").ToArray()),
            ["motivation"] = c.Motivation,
        };
    }
}
