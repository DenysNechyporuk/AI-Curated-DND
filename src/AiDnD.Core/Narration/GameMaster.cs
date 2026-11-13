using AiDnD.Core.Game;
using AiDnD.Core.Llm;
using AiDnD.Core.Tools;

namespace AiDnD.Core.Narration;

public sealed class GameMasterOptions
{
    /// <summary>Скільки разів поспіль модель може викликати інструменти за один хід (захист від зациклення).</summary>
    public int MaxToolRounds { get; set; } = 8;
    /// <summary>Коли в історії стає більше повідомлень — старі стискаються в підсумок.</summary>
    public int HistorySoftLimit { get; set; } = 30;
    /// <summary>Скільки останніх повідомлень залишати "як є" після стиснення.</summary>
    public int HistoryKeep { get; set; } = 12;
    public double Temperature { get; set; } = 0.8;
}

public sealed record TurnResult(string Narration, IReadOnlyList<ToolExecution> ToolExecutions, LlmUsage Usage);

/// <summary>
/// Оркестратор ходу: Гравець → LLM → (інструменти C#)* → LLM → опис.
/// Це "серце" архітектури: модель лише просить виконати дії, а виконує їх код.
/// </summary>
public sealed class GameMaster
{
    private readonly ILlmProvider _llm;
    private readonly ToolRegistry _tools;
    private readonly ToolContext _context;
    private readonly CampaignMemory _memory;
    private readonly GameMasterOptions _options;

    public GameMaster(ILlmProvider llm, GameState state, RulesEngine rules, ToolRegistry? tools = null, GameMasterOptions? options = null)
    {
        _llm = llm;
        _tools = tools ?? ToolRegistry.CreateDefault();
        _options = options ?? new GameMasterOptions();
        _context = new ToolContext(state, rules);
        _memory = new CampaignMemory(llm);
    }

    public GameState State => _context.State;
    public ILlmProvider Provider => _llm;

    /// <summary>Подія для UI: показати кидок кубика одразу, як він стався.</summary>
    public event Action<ToolExecution>? ToolExecuted;

    public async Task<TurnResult> PlayTurnAsync(string playerInput, CancellationToken ct = default)
    {
        var history = State.History;
        history.Add(ChatMessage.User(playerInput));

        var executions = new List<ToolExecution>();
        int inTokens = 0, outTokens = 0;
        string? narration = null;

        for (int round = 0; round <= _options.MaxToolRounds; round++)
        {
            // В останньому раунді інструменти не передаються — змушуємо модель просто описати результат.
            bool allowTools = round < _options.MaxToolRounds;
            var response = await _llm.CompleteAsync(new LlmRequest
            {
                SystemPrompt = Prompts.BuildGameMasterPrompt(State), // стан підставляється свіжий на кожному раунді
                Messages = history.ToList(), // знімок, а не живий список
                Tools = allowTools ? _tools.Definitions : [],
                Temperature = _options.Temperature,
            }, ct);

            inTokens += response.Usage?.InputTokens ?? 0;
            outTokens += response.Usage?.OutputTokens ?? 0;

            history.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = response.Text,
                ToolCalls = response.ToolCalls,
                ProviderData = response.ProviderData,
                ProviderName = _llm.Name,
            });

            if (response.ToolCalls.Count == 0)
            {
                narration = response.Text;
                break;
            }

            var results = new List<ToolResult>();
            foreach (var call in response.ToolCalls)
            {
                var exec = _tools.Execute(call, _context);
                executions.Add(exec);
                ToolExecuted?.Invoke(exec);
                results.Add(new ToolResult { CallId = call.Id, Name = call.Name, Content = exec.Result });
            }
            history.Add(ChatMessage.Tool(results));
        }

        State.Turn++;
        await _memory.CompactAsync(State, _options.HistorySoftLimit, _options.HistoryKeep, ct);

        return new TurnResult(
            string.IsNullOrWhiteSpace(narration) ? "(Майстер мовчки спостерігає за вами...)" : narration.Trim(),
            executions,
            new LlmUsage(inTokens, outTokens));
    }
}
