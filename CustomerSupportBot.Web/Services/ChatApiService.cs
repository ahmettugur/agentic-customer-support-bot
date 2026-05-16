using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Chat endpoint'leri için C# servis katmanı.
/// chat-api-client.js'teki sendMessage, submitRating vb. çağrıların karşılığı.
/// SSE streaming (sendMessageStream) JS interop yoluyla kalır; bu servis yalnızca
/// non-streaming JSON endpoint'lerini kapsar.
/// </summary>
public sealed class ChatApiService(HttpClient http)
{
    public async Task<RatingResponse?> SubmitRatingAsync(string sessionId, int stars, string? feedback)
    {
        var response = await http.PostAsJsonAsync($"/sessions/{sessionId}/rating",
            new { stars, feedback });
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<RatingResponse>();
    }

    public async Task<RatingResponse?> GetRatingAsync(string sessionId)
    {
        try
        {
            return await http.GetFromJsonAsync<RatingResponse>($"/sessions/{sessionId}/rating");
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<SessionInfo>> GetSessionsAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<SessionInfo>>("/sessions/");
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<List<SessionMessage>> GetSessionMessagesAsync(string sessionId)
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<SessionMessage>>($"/sessions/{sessionId}/messages");
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async IAsyncEnumerable<(string Type, string Data)> StreamChatAsync(
        string query,
        string? sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        HttpResponseMessage resp;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/chat/stream");
            req.Content = JsonContent.Create(new { query, sessionId });
            req.Headers.Accept.ParseAdd("text/event-stream");
            req.SetBrowserRequestStreamingEnabled(true); // Blazor WASM: stream chunks as they arrive
            resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch { yield break; }

        if (!resp.IsSuccessStatusCode) { resp.Dispose(); yield break; }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string evType = "message";
        var dataLines = new List<string>();

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(ct); }
            catch { yield break; }

            if (line is null) break;

            if      (line.StartsWith("event:")) evType = line[6..].Trim();
            else if (line.StartsWith("data:"))  dataLines.Add(line[5..].Trim());
            else if (line.Length == 0 && dataLines.Count > 0)
            {
                yield return (evType, string.Join('\n', dataLines));
                evType = "message";
                dataLines.Clear();
            }
        }

        resp.Dispose();
    }
}

public sealed record RatingResponse(int Stars, string? Feedback);
public sealed record SessionInfo(string SessionId, DateTimeOffset LastActivity, int MessageCount);
public sealed record SessionMessage(string Role, string Content, DateTimeOffset Timestamp);
