using AiDnD.Core.Llm.Providers;

namespace AiDnD.Core.Llm;

public sealed class LlmSettings
{
    /// <summary>"gemini" або "ollama".</summary>
    public string Provider { get; set; } = GeminiProvider.ProviderName;
    public GeminiOptions Gemini { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
}

/// <summary>Створює провайдера за назвою з конфігурації — решта гри про конкретну модель не знає.</summary>
public static class LlmProviderFactory
{
    public static ILlmProvider Create(LlmSettings settings) => settings.Provider.ToLowerInvariant() switch
    {
        GeminiProvider.ProviderName => new GeminiProvider(settings.Gemini),
        OllamaProvider.ProviderName => new OllamaProvider(settings.Ollama),
        _ => throw new LlmException($"Невідомий провайдер '{settings.Provider}'. Доступні: gemini, ollama."),
    };
}
