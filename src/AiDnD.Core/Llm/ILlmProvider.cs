namespace AiDnD.Core.Llm;

/// <summary>
/// Спільний інтерфейс для будь-якої мовної моделі (хмарної чи локальної).
/// Гра працює лише з цим інтерфейсом, тому провайдера можна змінити однією стрічкою конфігурації.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Унікальна назва провайдера (gemini, ollama...).</summary>
    string Name { get; }

    /// <summary>Назва моделі, напр. gemini-2.5-flash.</summary>
    string Model { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
