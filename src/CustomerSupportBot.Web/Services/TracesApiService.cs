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
public sealed class TracesApiService(HttpClient http, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private DateTimeOffset _retryAt;

    private async Task<T?> ReadAsync<T>(string path)
    {
        // Serialize trace reads so queued detail requests also respect a newly received 429.
        await _readGate.WaitAsync();
        try
        {
            if (_clock.GetUtcNow() < _retryAt)
                throw new HttpRequestException("Trace request cooldown is active.", null,
                    System.Net.HttpStatusCode.TooManyRequests);

            using var response = await http.GetAsync(path);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var now = _clock.GetUtcNow();
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date - now) ?? TimeSpan.FromMinutes(1);
                _retryAt = now + (delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1));
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>();
        }
        finally { _readGate.Release(); }
    }

    public async Task<List<TraceSession>> GetSessionsAsync()
    {
        // Let the page retain its last successful list when a refresh fails.
        return await ReadAsync<List<TraceSession>>("/traces/sessions") ?? [];
    }

    public async Task<TraceDetail?> GetTraceAsync(string traceId)
    {
        try
        {
            return await ReadAsync<TraceDetail>($"/traces/{Uri.EscapeDataString(traceId)}");
        }
        catch { return null; }
    }

    public async Task<List<TraceDetail>> GetBySessionAsync(string sessionId)
    {
        try
        {
            var result = await ReadAsync<List<TraceDetail>>($"/traces/by-session/{Uri.EscapeDataString(sessionId)}");
            return result ?? [];
        }
        catch { return []; }
    }

    public async Task<List<TraceDetail>> GetRecentAsync(int count = 50)
    {
        try
        {
            var result = await ReadAsync<List<TraceDetail>>($"/traces/recent?count={count}");
            return result ?? [];
        }
        catch { return []; }
    }

    public async Task<List<BridgeChatMessage>> GetBridgeHistoryAsync(string sessionId, int take = 100)
    {
        try
        {
            var result = await ReadAsync<List<BridgeChatMessage>>(
                $"/chat-sessions/{Uri.EscapeDataString(sessionId)}/history?take={take}");
            return result ?? [];
        }
        catch { return []; }
    }

    public async Task<List<ApprovalRequest>> GetApprovalsBySessionAsync(string sessionId)
    {
        try
        {
            var all = await ReadAsync<List<ApprovalRequest>>("/approvals/recent?count=200");
            return all?.Where(a => a.SessionId == sessionId).ToList() ?? [];
        }
        catch { return []; }
    }

    public async Task<List<EscalationRequest>> GetEscalationsBySessionAsync(string sessionId)
    {
        try
        {
            var all = await ReadAsync<List<EscalationRequest>>("/escalations/recent?count=200");
            return all?.Where(e => e.SessionId == sessionId).ToList() ?? [];
        }
        catch { return []; }
    }
}
