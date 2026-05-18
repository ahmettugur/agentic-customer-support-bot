using System.Net.Http.Json;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// admin.js'teki ADMIN_API ve AGENT_API endpoint'lerini C# HttpClient ile sarar.
/// Role tespiti AppAuthStateProvider üzerinden yapılır; endpoint prefix otomatik seçilir.
/// </summary>
public sealed class AdminApiService(HttpClient http, AppAuthStateProvider authState)
{
    // ─── Role-aware prefix ───────────────────────────────────────────────────

    private async Task<string> PrefixAsync()
    {
        var state = await authState.GetAuthenticationStateAsync();
        var role = state.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        return role == "Agent" ? "/agent" : string.Empty;
    }

    // ─── Approvals ───────────────────────────────────────────────────────────

    public async Task<List<ApprovalRequest>> GetPendingApprovalsAsync()
    {
        var prefix = await PrefixAsync();
        var result = await http.GetFromJsonAsync<List<ApprovalRequest>>($"{prefix}/approvals/pending");
        return result ?? [];
    }

    public async Task<List<ApprovalRequest>> GetRecentApprovalsAsync(int count = 50)
    {
        var prefix = await PrefixAsync();
        var result = await http.GetFromJsonAsync<List<ApprovalRequest>>($"{prefix}/approvals/recent?count={count}");
        return result ?? [];
    }

    public async Task ApproveAsync(string id, string? reason, string decidedBy = "admin")
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/approvals/{id}/approve",
            new { decidedBy, reason });
        response.EnsureSuccessStatusCode();
    }

    public async Task RejectAsync(string id, string? reason, string decidedBy = "admin")
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/approvals/{id}/reject",
            new { decidedBy, reason });
        response.EnsureSuccessStatusCode();
    }

    // ─── Escalations ─────────────────────────────────────────────────────────

    public async Task<List<EscalationRequest>> GetOpenEscalationsAsync()
    {
        var prefix = await PrefixAsync();
        var result = await http.GetFromJsonAsync<List<EscalationRequest>>($"{prefix}/escalations/open");
        return result ?? [];
    }

    public async Task<List<EscalationRequest>> GetRecentEscalationsAsync(int count = 50)
    {
        var prefix = await PrefixAsync();
        var result = await http.GetFromJsonAsync<List<EscalationRequest>>($"{prefix}/escalations/recent?count={count}");
        return result ?? [];
    }

    public async Task AcknowledgeAsync(string id, string assignedTo)
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/escalations/{id}/acknowledge",
            new { assignedTo });
        response.EnsureSuccessStatusCode();
    }

    public async Task ResolveEscalationAsync(string id, string resolution, string assignedTo = "admin")
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/escalations/{id}/resolve",
            new { resolution, assignedTo });
        response.EnsureSuccessStatusCode();
    }

    public async Task DismissAsync(string id, string resolution)
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/escalations/{id}/dismiss",
            new { resolution });
        response.EnsureSuccessStatusCode();
    }

    public async Task ReplanEscalationAsync(string id, string? note, string requestedBy = "admin")
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/escalations/{id}/replan",
            new { requestedBy, note });
        response.EnsureSuccessStatusCode();
    }

    // ─── Chat Sessions ────────────────────────────────────────────────────────

    public async Task<List<ActiveChatSession>> GetActiveChatsAsync()
    {
        var prefix = await PrefixAsync();
        var result = await http.GetFromJsonAsync<List<ActiveChatSession>>($"{prefix}/chat-sessions/active");
        return result ?? [];
    }

    public async Task<List<ChatHistoryMessage>> GetChatHistoryAsync(string sessionId, int take = 200)
    {
        var prefix = await PrefixAsync();
        var result = await http.GetFromJsonAsync<List<ChatHistoryMessage>>(
            $"{prefix}/chat-sessions/{sessionId}/history?take={take}");
        return result ?? [];
    }

    public async Task TakeoverAsync(string sessionId, string humanAgent)
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/chat-sessions/{sessionId}/takeover",
            new { humanAgent });
        response.EnsureSuccessStatusCode();
    }

    public async Task ReleaseAsync(string sessionId)
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/chat-sessions/{sessionId}/release",
            new { });
        response.EnsureSuccessStatusCode();
    }

    public async Task SendChatMessageAsync(string sessionId, string text)
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/chat-sessions/{sessionId}/messages",
            new { text });
        response.EnsureSuccessStatusCode();
    }

    public async Task ReplanChatAsync(string sessionId, string? note = null, string requestedBy = "admin")
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/chat-sessions/{sessionId}/replan",
            new { requestedBy, note });
        response.EnsureSuccessStatusCode();
    }

    // ─── Agents ──────────────────────────────────────────────────────────────

    public async Task<List<AgentInfo>> GetAgentsAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<AgentListResponse>("/agents");
            return result?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed record AgentListResponse(int Count, List<AgentInfo> Items);

    // ─── Sessions ────────────────────────────────────────────────────────────

    public async Task<List<SessionSummary>> GetSessionsAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<SessionSummary>>("/sessions/");
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ─── Chat Sentiment ───────────────────────────────────────────────────────

    public async Task<ChatSentiment?> GetSentimentAsync(string sessionId)
    {
        try
        {
            var prefix = await PrefixAsync();
            return await http.GetFromJsonAsync<ChatSentiment>(
                $"{prefix}/chat-sessions/{sessionId}/sentiment");
        }
        catch { return null; }
    }

    // ─── Improvements ─────────────────────────────────────────────────────────

    public async Task<List<LessonProposal>> GetLessonsAsync(string status)
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<LessonProposal>>($"/improvements?status={status}");
            return result ?? [];
        }
        catch { return []; }
    }

    public async Task<(int Candidates, int Proposed, string? Error)> MineImprovementsAsync()
    {
        try
        {
            var resp = await http.PostAsync("/improvements/mine", null);
            if (!resp.IsSuccessStatusCode) return (0, 0, $"HTTP {(int)resp.StatusCode}");
            var j = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var candidates = j.TryGetProperty("candidates",      out var c) ? c.GetInt32() : 0;
            var proposed   = j.TryGetProperty("proposedLessons", out var p) ? p.GetInt32() : 0;
            var error      = j.TryGetProperty("error",           out var e) && e.ValueKind != System.Text.Json.JsonValueKind.Null ? e.GetString() : null;
            return (candidates, proposed, error);
        }
        catch (Exception ex) { return (0, 0, ex.Message); }
    }

    public async Task ApproveLessonAsync(string id, string? reason = null)
    {
        try { await http.PostAsJsonAsync($"/improvements/{Uri.EscapeDataString(id)}/approve", new { reason }); } catch { }
    }

    public async Task RejectLessonAsync(string id, string? reason = null)
    {
        try { await http.PostAsJsonAsync($"/improvements/{Uri.EscapeDataString(id)}/reject", new { reason }); } catch { }
    }
}

public sealed record ChatSentiment(string? Sentiment, double Score);
