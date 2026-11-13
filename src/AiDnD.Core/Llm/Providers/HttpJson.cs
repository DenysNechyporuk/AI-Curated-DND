using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace AiDnD.Core.Llm.Providers;

/// <summary>Спільна логіка POST-запитів із JSON і повторними спробами при перевантаженні сервера.</summary>
internal static class HttpJson
{
    private static readonly HttpStatusCode[] Retryable =
        [HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway, HttpStatusCode.GatewayTimeout, HttpStatusCode.InternalServerError];

    public static async Task<JsonNode> PostAsync(HttpClient http, string url, JsonNode body,
        Action<HttpRequestMessage>? configure, string providerName, CancellationToken ct, int maxAttempts = 3)
    {
        var payload = body.ToJsonString();
        for (int attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            configure?.Invoke(req);

            HttpResponseMessage resp;
            try
            {
                resp = await http.SendAsync(req, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new LlmException($"[{providerName}] Не вдалося підключитися: {ex.Message}", ex);
            }

            using (resp)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode)
                    return JsonNode.Parse(text) ?? throw new LlmException($"[{providerName}] Порожня відповідь.");

                if (attempt < maxAttempts && Retryable.Contains(resp.StatusCode))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                    continue;
                }

                throw new LlmException($"[{providerName}] HTTP {(int)resp.StatusCode}: {Truncate(text, 600)}");
            }
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
