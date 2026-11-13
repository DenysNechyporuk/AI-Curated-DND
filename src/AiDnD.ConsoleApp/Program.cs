using System.Text;
using System.Text.Json;
using AiDnD.Core.Game;
using AiDnD.Core.Llm;
using AiDnD.Core.Narration;
using AiDnD.Core.Persistence;
using AiDnD.Core.Tools;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// ---------- Конфігурація ----------
var settings = LoadSettings(args);
ILlmProvider llm;
try
{
    llm = LlmProviderFactory.Create(settings);
}
catch (LlmException ex)
{
    Ui.Error(ex.Message);
    Ui.Info("Приклад: $env:GEMINI_API_KEY=\"ключ\"; dotnet run   або   dotnet run -- --provider ollama");
    return 1;
}

var repository = new JsonFileGameRepository(Path.Combine(AppContext.BaseDirectory, "saves"));
var rules = new RulesEngine(new DiceRoller());

Ui.Title("AI Dungeon Master — темне фентезі");
Ui.Info($"Модель: {llm.Name} / {llm.Model}. Команди: /help");

// ---------- Новий герой або завантаження ----------
GameState state;
var autosave = await repository.LoadAsync("autosave");
if (autosave is not null && Ui.Ask("Знайдено автозбереження. Продовжити? (т/н)").Trim().ToLowerInvariant() is "т" or "так" or "y" or "yes" or "")
{
    state = autosave;
    Ui.Info($"Завантажено: {state.CampaignName}, хід {state.Turn}.");
}
else
{
    var name = Ui.Ask("Як звати вашого героя? (Enter — Арен)").Trim();
    state = NpcTemplates.CreateNewCampaign(name.Length == 0 ? "Арен" : name);
}

var gm = new GameMaster(llm, state, rules);
gm.ToolExecuted += exec => Ui.Tool(exec);

if (state.Turn == 0)
    await RunTurn("[Початок гри. Опиши початкову сцену й представ героя player_1 у ній. Не роби дій за нього.]");
else if (state.History.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text)) is { } last)
    Ui.Narration(last.Text!);

// ---------- Головний цикл ----------
while (true)
{
    var input = Ui.Ask("\n>").Trim();
    if (input.Length == 0) continue;

    if (input.StartsWith('/'))
    {
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var arg = parts.Length > 1 ? parts[1] : "";
        switch (parts[0].ToLowerInvariant())
        {
            case "/quit" or "/exit" or "/q":
                await repository.SaveAsync(state, "autosave");
                Ui.Info("Збережено. До зустрічі біля вогнища.");
                return 0;
            case "/help":
                Ui.Info("""
                    Просто пишіть, що робить ваш герой: "Я питаю корчмарку про сина", "Атакую гобліна мечем".
                    /state          — стан гри (HP, NPC, локація)
                    /sheet [id]     — лист персонажа
                    /log            — журнал кидків
                    /memory         — підсумки попередніх сцен
                    /npc <опис>     — згенерувати NPC через ШІ (структурований JSON)
                    /roll <вираз>   — кинути кубики самому, напр. /roll 2d6+1
                    /save [слот] /load [слот]
                    /quit           — зберегти й вийти
                    """);
                break;
            case "/state":
                Ui.Info(state.ToPromptSnapshot());
                break;
            case "/sheet":
                var c = state.Find(arg.Length == 0 ? "player_1" : arg);
                if (c is null) Ui.Error("Немає такого персонажа.");
                else Ui.Info(JsonSerializer.Serialize(c, JsonFileGameRepository.Options));
                break;
            case "/log":
                foreach (var line in state.EventLog.TakeLast(20)) Ui.Info(line);
                break;
            case "/memory":
                if (state.SceneSummaries.Count == 0) Ui.Info("Поки що підсумків немає (з'являться, коли історія стане довгою).");
                foreach (var s in state.SceneSummaries) Ui.Info("• " + s);
                break;
            case "/roll":
                try { Ui.Info(rules.Dice.Roll(arg.Length == 0 ? "1d20" : arg).ToString()); }
                catch (GameRuleException ex) { Ui.Error(ex.Message); }
                break;
            case "/npc":
                try
                {
                    Ui.Info("Генерую NPC...");
                    var npc = await new NpcGenerator(llm, rules.Dice).GenerateAsync(arg.Length == 0 ? "випадковий мешканець" : arg, state);
                    Ui.Info($"{npc.Name} ({npc.Id}), {npc.CharacterClass}, HP {npc.Hp}, AC {npc.ArmorClass}\n{npc.Description}\nМотивація: {npc.Motivation}");
                }
                catch (LlmException ex) { Ui.Error(ex.Message); }
                break;
            case "/save":
                await repository.SaveAsync(state, arg.Length == 0 ? "manual" : arg);
                Ui.Info("Збережено.");
                break;
            case "/load":
                var loaded = await repository.LoadAsync(arg.Length == 0 ? "manual" : arg);
                if (loaded is null) { Ui.Error($"Слот не знайдено. Є: {string.Join(", ", repository.ListSlots())}"); break; }
                state = loaded;
                gm = new GameMaster(llm, state, rules);
                gm.ToolExecuted += exec => Ui.Tool(exec);
                Ui.Info($"Завантажено: хід {state.Turn}, {state.CurrentLocation.Name}.");
                break;
            default:
                Ui.Error("Невідома команда. /help");
                break;
        }
        continue;
    }

    await RunTurn(input);
    await repository.SaveAsync(state, "autosave");
}

async Task RunTurn(string text)
{
    try
    {
        Ui.Thinking();
        var result = await gm.PlayTurnAsync(text);
        Ui.Narration(result.Narration);
        Ui.Stats(result.Usage);
    }
    catch (LlmException ex)
    {
        // Повідомлення гравця вже в історії — прибираємо незавершений хід, щоб не зламати діалог
        TrimBrokenTurn(state);
        Ui.Error(ex.Message);
    }
}

static void TrimBrokenTurn(GameState s)
{
    int lastUser = s.History.FindLastIndex(m => m.Role == ChatRole.User);
    if (lastUser >= 0) s.History.RemoveRange(lastUser, s.History.Count - lastUser);
}

static LlmSettings LoadSettings(string[] args)
{
    var settings = new LlmSettings();
    foreach (var file in new[] { "appsettings.json", "appsettings.local.json" })
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        if (!File.Exists(path)) continue;
        var loaded = JsonSerializer.Deserialize<LlmSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (loaded is null) continue;
        settings.Provider = loaded.Provider;
        if (!string.IsNullOrWhiteSpace(loaded.Gemini.ApiKey)) settings.Gemini.ApiKey = loaded.Gemini.ApiKey;
        settings.Gemini.Model = loaded.Gemini.Model;
        settings.Ollama = loaded.Ollama;
    }

    if (Environment.GetEnvironmentVariable("GEMINI_API_KEY") is { Length: > 0 } key) settings.Gemini.ApiKey = key;
    if (Environment.GetEnvironmentVariable("GEMINI_MODEL") is { Length: > 0 } model) settings.Gemini.Model = model;

    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--provider") settings.Provider = args[i + 1];
        if (args[i] == "--model")
        {
            settings.Gemini.Model = args[i + 1];
            settings.Ollama.Model = args[i + 1];
        }
    }
    return settings;
}

static class Ui
{
    public static void Title(string t) => Write($"\n=== {t} ===\n", ConsoleColor.DarkYellow);
    public static void Info(string t) => Write(t, ConsoleColor.Gray);
    public static void Error(string t) => Write("! " + t, ConsoleColor.Red);
    public static void Narration(string t) => Write("\n" + t, ConsoleColor.White);
    public static void Thinking() => Write("...", ConsoleColor.DarkGray);
    public static void Stats(LlmUsage u) => Write($"[токени: вхід {u.InputTokens}, вихід {u.OutputTokens}]", ConsoleColor.DarkGray);

    public static void Tool(ToolExecution e)
    {
        var color = e.IsError ? ConsoleColor.DarkRed : ConsoleColor.DarkCyan;
        Write($"  ⚙ {e.Call.Name}({e.Call.Arguments.ToJsonString()}) → {e.Result.ToJsonString()}", color);
    }

    public static string Ask(string prompt)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(prompt + " ");
        Console.ResetColor();
        return Console.ReadLine() ?? "/quit";
    }

    private static void Write(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
