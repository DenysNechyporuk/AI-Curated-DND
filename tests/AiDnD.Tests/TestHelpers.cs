using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AiDnD.Core.Game;
using AiDnD.Core.Llm;

namespace AiDnD.Tests;

/// <summary>Повертає заздалегідь задані числа — кидки стають передбачуваними.</summary>
public sealed class FixedRandom(params int[] values) : IRandomSource
{
    private int _i;
    public int Next(int minInclusive, int maxInclusive) => Math.Clamp(values[_i++ % values.Length], minInclusive, maxInclusive);
}

/// <summary>Фейкова модель: відповідає за сценарієм і запам'ятовує запити.</summary>
public sealed class ScriptedLlm(params LlmResponse[] responses) : ILlmProvider
{
    private readonly Queue<LlmResponse> _queue = new(responses);
    public List<LlmRequest> Requests { get; } = [];
    public string Name => "scripted";
    public string Model => "test";

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(_queue.Count > 0 ? _queue.Dequeue() : new LlmResponse { Text = "кінець" });
    }

    public static LlmResponse Call(string name, JsonObject args) =>
        new() { ToolCalls = [new ToolCall { Name = name, Arguments = args }] };
}

public sealed class FakeHttpHandler(Func<HttpRequestMessage, string, string> respond) : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Bodies.Add(body);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(respond(request, body), Encoding.UTF8, "application/json"),
        };
    }
}
