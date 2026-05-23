using System.Net.Http.Json;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Services;

public sealed record TraceSession(
    string SessionId,
    string? Title,
    int TraceCount,
    int MessageCount,
    DateTimeOffset LastTraceAt
);

public sealed record SessionChatMessage(string Role, string Text);

public sealed record BridgeChatMessage(
    string Sender,
    string Text,
    string? HumanAgent,
    DateTime Timestamp
);

/// <summary>
/// Trace dashboard API servisi.
/// traces.js'teki window.Auth.fetch('/traces/sessions') çağrısının karşılığı.
/// </summary>
public sealed class TracesApiService(HttpClient http)
{
    public async Task<List<TraceSession>> GetSessionsAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<TraceSession>>("/traces/sessions");
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<TraceDetail?> GetTraceAsync(string traceId)
    {
        try
        {
            return await http.GetFromJsonAsync<TraceDetail>($"/traces/{Uri.EscapeDataString(traceId)}");
        }
        catch { return null; }
    }

    public async Task<List<TraceDetail>> GetBySessionAsync(string sessionId)
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<TraceDetail>>($"/traces/by-session/{Uri.EscapeDataString(sessionId)}");
            return result ?? [];
        }
        catch { return []; }
    }

    public async Task<List<TraceDetail>> GetRecentAsync(int count = 50)
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<TraceDetail>>($"/traces/recent?count={count}");
            return result ?? [];
        }
        catch { return []; }
    }

    public async Task<List<BridgeChatMessage>> GetBridgeHistoryAsync(string sessionId, int take = 100)
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<BridgeChatMessage>>(
                $"/chat-sessions/{Uri.EscapeDataString(sessionId)}/history?take={take}");
            return result ?? [];
        }
        catch { return []; }
    }
}
