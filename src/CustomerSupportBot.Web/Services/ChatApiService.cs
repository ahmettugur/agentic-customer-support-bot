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

    /// <summary>
    /// Fotoğrafı yükler (<c>POST /chat/attachments</c>). <paramref name="sessionId"/> boşsa sunucu
    /// oturum açar ve döndürür. Başarısızsa <see cref="AttachmentUploadResponse.Error"/> kullanıcıya
    /// gösterilecek metni taşır.
    /// </summary>
    public async Task<AttachmentUploadResponse> UploadAttachmentAsync(
        byte[] data, string fileName, string contentType, string? sessionId)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(data);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(file, "file", fileName);
            if (!string.IsNullOrEmpty(sessionId)) form.Add(new StringContent(sessionId), "sessionId");

            var response = await http.PostAsync("/chat/attachments", form);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<AttachmentUploadResponse>()
                       ?? new AttachmentUploadResponse(null, null, null, "Fotoğraf yüklenemedi.");

            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            return new AttachmentUploadResponse(null, null, null, error?.Message ?? "Fotoğraf yüklenemedi.");
        }
        catch
        {
            return new AttachmentUploadResponse(null, null, null, "Fotoğraf yüklenemedi.");
        }
    }

    /// <summary>Gönderilmemiş fotoğrafı sunucudan da kaldırır; hata kritik değil.</summary>
    public async Task DeleteAttachmentAsync(string attachmentId)
    {
        try { await http.DeleteAsync($"/chat/attachments/{attachmentId}"); }
        catch { /* önizlemeden zaten kalktı; gönderilmeyen fotoğraf hiçbir onaya bağlanmaz */ }
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
public sealed record AttachmentUploadResponse(string? AttachmentId, string? SessionId, string? Description, string? Error = null);
public sealed record ApiError(string? Error, string? Message);
public sealed record SessionInfo(string SessionId, DateTimeOffset LastActivity, int MessageCount);
public sealed record SessionMessage(string Role, string Content, DateTimeOffset Timestamp);

public sealed record UnseenApproval(
    string Id,
    string ToolName,
    string Status,
    string? DecisionReason,
    string? ExecutionResult,
    DateTimeOffset? DecidedAt,
    string? ExecutionStatus = null);

public sealed record ApprovalHistoryItem(
    string Id,
    string ToolName,
    string Status,
    string? DecisionReason,
    string? ExecutionResult,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? ExecutionStatus = null);
