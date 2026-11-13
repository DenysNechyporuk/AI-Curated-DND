using System.Text;
using AiDnD.Core.Game;
using AiDnD.Core.Llm;

namespace AiDnD.Core.Narration;

/// <summary>
/// Пам'ять кампанії через підсумки (summarization): старі повідомлення стискаються LLM у кілька речень,
/// які зберігаються в <see cref="GameState.SceneSummaries"/> і підставляються в кожен наступний запит.
/// Наступний крок розвитку — RAG (embeddings + векторний пошук).
/// </summary>
public sealed class CampaignMemory(ILlmProvider llm)
{
    public async Task CompactAsync(GameState state, int softLimit, int keep, CancellationToken ct = default)
    {
        var history = state.History;
        if (history.Count <= softLimit) return;

        // Ріжемо лише по межі повідомлення гравця, щоб не розірвати пару "виклик інструмента → результат".
        int cut = history.Count - keep;
        while (cut < history.Count && history[cut].Role != ChatRole.User) cut++;
        if (cut <= 0 || cut >= history.Count) return;

        var old = history.Take(cut).ToList();
        var summary = await SummarizeAsync(old, ct);
        if (!string.IsNullOrWhiteSpace(summary)) state.SceneSummaries.Add(summary.Trim());
        history.RemoveRange(0, cut);
    }

    public async Task<string> SummarizeAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var transcript = new StringBuilder();
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.User: transcript.AppendLine($"ГРАВЕЦЬ: {m.Text}"); break;
                case ChatRole.Assistant when !string.IsNullOrWhiteSpace(m.Text): transcript.AppendLine($"МАЙСТЕР: {m.Text}"); break;
                case ChatRole.Tool:
                    foreach (var r in m.ToolResults) transcript.AppendLine($"[{r.Name}] {r.Content.ToJsonString()}");
                    break;
            }
        }

        var response = await llm.CompleteAsync(new LlmRequest
        {
            SystemPrompt = Prompts.Summarizer,
            Messages = [ChatMessage.User(transcript.ToString())],
            Temperature = 0.2,
        }, ct);
        return response.Text ?? "";
    }
}
