using System.Text.Json.Nodes;
using AiDnD.Core.Game;
using AiDnD.Core.Llm;
using AiDnD.Core.Llm.Providers;
using AiDnD.Core.Narration;
using AiDnD.Core.Persistence;

namespace AiDnD.Tests;

public class GameMasterTests
{
    [Fact]
    public async Task Turn_ExecutesToolAndSendsResultBack()
    {
        var state = NpcTemplates.CreateNewCampaign("Тест");
        var goblin = NpcTemplates.Create("goblin");
        goblin.Id = "goblin_1";
        state.Characters.Add(goblin);

        var llm = new ScriptedLlm(
            ScriptedLlm.Call("attack", new JsonObject { ["attacker_id"] = "player_1", ["target_id"] = "goblin_1", ["weapon"] = "Довгий меч" }),
            new LlmResponse { Text = "Твій клинок прорізає повітря..." });

        var gm = new GameMaster(llm, state, new RulesEngine(new DiceRoller(new FixedRandom(20, 8, 8))));
        var result = await gm.PlayTurnAsync("Я атакую гобліна мечем");

        Assert.Equal("Твій клинок прорізає повітря...", result.Narration);
        Assert.Single(result.ToolExecutions);
        Assert.Equal(0, goblin.Hp); // крит: 2d8 (8+8) + 3 = 19 урону — HP змінив код, а не модель

        // Другий запит до моделі вже містить результат інструмента і оновлений стан
        var second = llm.Requests[1];
        Assert.Equal(ChatRole.Tool, second.Messages[^1].Role);
        Assert.Contains("повалений", second.SystemPrompt);
    }

    [Fact]
    public async Task Turn_StopsInfiniteToolLoop()
    {
        var state = NpcTemplates.CreateNewCampaign("Тест");
        var responses = Enumerable.Range(0, 20)
            .Select(_ => ScriptedLlm.Call("roll_dice", new JsonObject { ["expression"] = "1d6" })).ToArray();
        var llm = new ScriptedLlm(responses);

        var gm = new GameMaster(llm, state, new RulesEngine(new DiceRoller()), options: new GameMasterOptions { MaxToolRounds = 3 });
        await gm.PlayTurnAsync("кидаю");

        Assert.Equal(4, llm.Requests.Count);
        Assert.Empty(llm.Requests[^1].Tools); // останній запит — без інструментів
    }

    [Fact]
    public async Task Memory_SummarizesOldHistory()
    {
        var state = NpcTemplates.CreateNewCampaign("Тест");
        var llm = new ScriptedLlm();
        var gm = new GameMaster(llm, state, new RulesEngine(new DiceRoller()), options: new GameMasterOptions { HistorySoftLimit = 6, HistoryKeep = 2 });

        for (int i = 0; i < 4; i++) await gm.PlayTurnAsync($"дія {i}");

        Assert.NotEmpty(state.SceneSummaries);
        Assert.True(state.History.Count <= 6);
        Assert.Equal(ChatRole.User, state.History[0].Role);
    }

    [Fact]
    public async Task Save_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aidnd_" + Guid.NewGuid().ToString("N"));
        var repo = new JsonFileGameRepository(dir);
        var state = NpcTemplates.CreateNewCampaign("Тест");
        state.History.Add(ChatMessage.User("привіт"));

        await repo.SaveAsync(state, "slot");
        var loaded = await repo.LoadAsync("slot");

        Assert.NotNull(loaded);
        Assert.Equal(state.Characters.Count, loaded!.Characters.Count);
        Assert.Equal("Тест", loaded.Find("player_1")!.Name);
        Assert.Single(loaded.History);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void NpcGenerator_ClampsModelValues()
    {
        var gen = new NpcGenerator(new ScriptedLlm(), new DiceRoller());
        var npc = gen.ToCharacter(new NpcBlueprint { Name = "Велет", Hp = 9999, ArmorClass = 50, Str = 40, WeaponDamage = "дуже багато" });
        Assert.Equal(60, npc.MaxHp);
        Assert.Equal(20, npc.ArmorClass);
        Assert.Equal(18, npc.Abilities.Str);
        Assert.Equal("1d6", npc.Weapons[0].DamageDice);
    }
}

public class GeminiProviderTests
{
    [Fact]
    public async Task ParsesFunctionCall_AndEchoesRawContentBack()
    {
        var handler = new FakeHttpHandler((_, body) => """
            {"candidates":[{"content":{"role":"model","parts":[
              {"functionCall":{"name":"roll_dice","args":{"expression":"1d20"}},"thoughtSignature":"SIG123"}
            ]}}],"usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":5}}
            """);
        var provider = new GeminiProvider(new GeminiOptions { ApiKey = "test" }, new HttpClient(handler));

        var tool = new LlmModels_ToolDef();
        var response = await provider.CompleteAsync(new LlmRequest
        {
            SystemPrompt = "sys",
            Messages = [ChatMessage.User("кинь")],
            Tools = [tool.Def],
        });

        Assert.Single(response.ToolCalls);
        Assert.Equal("roll_dice", response.ToolCalls[0].Name);
        Assert.Equal("1d20", response.ToolCalls[0].Arguments["expression"]!.ToString());
        Assert.Equal(100, response.Usage!.InputTokens);
        Assert.Contains("functionDeclarations", handler.Bodies[0]);

        // Наступний запит має повернути thoughtSignature без змін
        await provider.CompleteAsync(new LlmRequest
        {
            SystemPrompt = "sys",
            Messages =
            [
                ChatMessage.User("кинь"),
                new ChatMessage { Role = ChatRole.Assistant, ToolCalls = response.ToolCalls, ProviderData = response.ProviderData, ProviderName = "gemini" },
                ChatMessage.Tool([new ToolResult { Name = "roll_dice", Content = new JsonObject { ["total"] = 12 } }]),
            ],
        });
        Assert.Contains("SIG123", handler.Bodies[1]);
        Assert.Contains("functionResponse", handler.Bodies[1]);
    }

    private sealed class LlmModels_ToolDef
    {
        public ToolDefinition Def { get; } = new AiDnD.Core.Tools.RollDiceTool().ToDefinition();
    }
}
