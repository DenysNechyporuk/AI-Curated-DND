using System.Text.Json.Nodes;
using AiDnD.Core.Game;
using AiDnD.Core.Tools;

namespace AiDnD.Tests;

public class DiceTests
{
    [Theory]
    [InlineData("2d6+3", new[] { 4, 5 }, 12)]
    [InlineData("d20", new[] { 17 }, 17)]
    [InlineData("1d8-1", new[] { 1 }, 0)]
    [InlineData("1d6+1d4+2", new[] { 6, 3 }, 11)]
    public void Roll_ComputesTotal(string expr, int[] rolls, int expected)
    {
        var roller = new DiceRoller(new FixedRandom(rolls));
        Assert.Equal(expected, roller.Roll(expr).Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1d1")]
    [InlineData("500d6")]
    [InlineData("2d6+")]
    public void Roll_RejectsInvalid(string expr) =>
        Assert.Throws<GameRuleException>(() => new DiceRoller().Roll(expr));

    [Fact]
    public void Roll_CriticalDoublesDice()
    {
        var r = new DiceRoller(new FixedRandom(3)).Roll("1d8", diceMultiplier: 2);
        Assert.Equal(2, r.Rolls.Count);
        Assert.Equal(6, r.Total);
    }
}

public class RulesEngineTests
{
    private static (GameState, Character hero, Character goblin) Setup()
    {
        var state = new GameState();
        var hero = NpcTemplates.CreateDefaultHero("Тест");
        var goblin = NpcTemplates.Create("goblin");
        goblin.Id = "goblin_1";
        state.Characters.AddRange([hero, goblin]);
        return (state, hero, goblin);
    }

    [Fact]
    public void Attack_HitDealsDamage()
    {
        var (_, hero, goblin) = Setup();
        // d20 = 15 (+3 Str +2 prof = 20 vs AC 15), 1d8 = 4 (+3) = 7 урону → гоблін (7 HP) повалений
        var rules = new RulesEngine(new DiceRoller(new FixedRandom(15, 4)));
        var r = rules.Attack(hero, goblin, "меч");

        Assert.True(r.Hit);
        Assert.Equal(7, r.Damage);
        Assert.Equal(0, goblin.Hp);
        Assert.True(r.TargetKilled);
    }

    [Fact]
    public void Attack_NaturalOneAlwaysMisses()
    {
        var (_, hero, goblin) = Setup();
        var r = new RulesEngine(new DiceRoller(new FixedRandom(1))).Attack(hero, goblin, null);
        Assert.False(r.Hit);
        Assert.Equal(7, goblin.Hp);
    }

    [Fact]
    public void Attack_UnknownWeaponIsRuleError()
    {
        var (_, hero, goblin) = Setup();
        Assert.Throws<GameRuleException>(() => new RulesEngine(new DiceRoller()).Attack(hero, goblin, "лазерна гармата"));
    }

    [Fact]
    public void SkillCheck_AddsProficiency()
    {
        var (_, hero, _) = Setup();
        // athletics: Str +3, proficiency +2, d20 = 10 → 15
        var r = new RulesEngine(new DiceRoller(new FixedRandom(10))).SkillCheck(hero, "athletics", 15);
        Assert.Equal(15, r.Total);
        Assert.True(r.Success);
    }

    [Fact]
    public void ToolRegistry_ReturnsErrorInsteadOfThrowing()
    {
        var (state, _, _) = Setup();
        var ctx = new ToolContext(state, new RulesEngine(new DiceRoller()));
        var exec = ToolRegistry.CreateDefault().Execute(new()
        {
            Name = "attack",
            Arguments = new JsonObject { ["attacker_id"] = "player_1", ["target_id"] = "dragon_9" },
        }, ctx);

        Assert.True(exec.IsError);
        Assert.Contains("dragon_9", exec.Result["error"]!.ToString());
    }

    [Fact]
    public void SpawnNpc_CreatesUniqueIds()
    {
        var (state, _, _) = Setup();
        var ctx = new ToolContext(state, new RulesEngine(new DiceRoller()));
        var exec = ToolRegistry.CreateDefault().Execute(new()
        {
            Name = "spawn_npc",
            Arguments = new JsonObject { ["type"] = "wolf", ["count"] = 2 },
        }, ctx);

        Assert.False(exec.IsError);
        Assert.NotNull(state.Find("wolf_1"));
        Assert.NotNull(state.Find("wolf_2"));
    }
}
