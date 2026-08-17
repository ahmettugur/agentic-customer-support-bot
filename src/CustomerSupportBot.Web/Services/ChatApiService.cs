using System.Net.Http.Json;

namespace CustomerSupportBot.Web.Services;

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

    public async Task<List<UnseenApproval>> GetUnseenApprovalsAsync(string sessionId)
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<UnseenApproval>>(
                $"/chat-sessions/{sessionId}/approvals/unseen");
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task MarkApprovalSeenAsync(string sessionId, string approvalId)
    {
        try
        {
            await http.PostAsync($"/chat-sessions/{sessionId}/approvals/{approvalId}/seen", null);
        }
        catch { /* bildirim state'i sadece UI'da kalır, kritik değil */ }
    }

    public async Task<List<ApprovalHistoryItem>> GetApprovalHistoryAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<List<ApprovalHistoryItem>>("/customer/approvals/history");
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }
}

public sealed record RatingResponse(int Stars, string? Feedback);
public sealed record SessionInfo(string SessionId, DateTimeOffset LastActivity, int MessageCount);
public sealed record SessionMessage(string Role, string Content, DateTimeOffset Timestamp);

public sealed record UnseenApproval(
    string Id,
    string ToolName,
    string Status,
    string? DecisionReason,
    string? ExecutionResult,
    DateTimeOffset? DecidedAt);

public sealed record ApprovalHistoryItem(
    string Id,
    string ToolName,
    string Status,
    string? DecisionReason,
    string? ExecutionResult,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);
