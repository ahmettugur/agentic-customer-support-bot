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

    /// <summary>👍 (true) / 👎 (false) / oyu geri çek (null). Başarısızsa false döner; arayüz oyu eski haline çevirir.</summary>
    public async Task<bool> SubmitMessageFeedbackAsync(string sessionId, string text, bool? helpful, string? reason = null, string? comment = null)
    {
        try
        {
            var response = await http.PostAsJsonAsync($"/sessions/{sessionId}/messages/feedback", new { text, helpful, reason, comment });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
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

    /// <summary>Kişisel verilerimin tamamı (JSON metni) — indirilecek dosyanın içeriği. Hata/yetkisizse null.</summary>
    public async Task<string?> ExportMyDataAsync()
    {
        try
        {
            var response = await http.GetAsync("/customer/data/export");
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sohbet ve profil verilerimi siler (geri alınamaz). Başarısızsa kullanıcıya gösterilecek metin döner.</summary>
    public async Task<string?> EraseMyDataAsync()
    {
        try
        {
            var response = await http.DeleteAsync("/customer/data?confirm=true");
            if (response.IsSuccessStatusCode) return null;
            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            return error?.Message ?? "Verileriniz silinemedi; lütfen tekrar deneyin.";
        }
        catch
        {
            return "Verileriniz silinemedi; lütfen tekrar deneyin.";
        }
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
/// <summary>GET /sessions/ satırı. Alanlar sunucu şekliyle (SessionInfo) birebir: Title ilk kullanıcı mesajının başıdır.</summary>
public sealed record SessionInfo(string SessionId, string Title, DateTimeOffset LastActivity, int MessageCount);

/// <summary>GET /sessions/{id}/messages satırı: sunucu <c>{ role: "user"|"bot", text }</c> döndürür.</summary>
public sealed record SessionMessage(string Role, string Text, MessageFeedbackView? Feedback = null);

/// <summary>Müşterinin bir bot yanıtına verdiği oy (geçmiş yüklenirken sunucu ekler).</summary>
public sealed record MessageFeedbackView(bool Helpful, string? Reason);

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
