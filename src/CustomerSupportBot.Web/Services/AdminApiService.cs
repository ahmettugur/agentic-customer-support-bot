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

    // Onay kartı her yoklamada yeniden çizilir; fotoğraf bir kez indirilir. Kayıt değişmez
    // (fotoğraf düzenlenmez), dolayısıyla önbellek geçersizleşmez — oturum (scope) boyunca yaşar.
    private readonly Dictionary<string, Task<string?>> _attachmentCache = new();

    /// <summary>
    /// Onay kaydına bağlı fotoğraf, <c>&lt;img&gt;</c>'de gösterilecek data URL olarak. Uç yetki
    /// istediği için doğrudan <c>src</c> verilemez (tarayıcı Bearer başlığı eklemez). Bulunamazsa null.
    /// </summary>
    public Task<string?> GetAttachmentDataUrlAsync(string attachmentId)
    {
        if (!_attachmentCache.TryGetValue(attachmentId, out var task))
        {
            task = LoadAttachmentDataUrlAsync(attachmentId);
            _attachmentCache[attachmentId] = task;
        }
        return task;
    }

    private async Task<string?> LoadAttachmentDataUrlAsync(string attachmentId)
    {
        try
        {
            var prefix = await PrefixAsync();
            using var response = await http.GetAsync($"{prefix}/attachments/{Uri.EscapeDataString(attachmentId)}");
            if (!response.IsSuccessStatusCode)
            {
                _attachmentCache.Remove(attachmentId);   // geçici hata kalıcı olmasın
                return null;
            }
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not ("image/jpeg" or "image/png")) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            _attachmentCache.Remove(attachmentId);
            return null;
        }
    }

    // ─── Temsilci durumu ─────────────────────────────────────────────────────

    /// <summary>Panel açılışı: çevrimdışıysa çevrimiçi yapar, uzaktaysa korur. Bağlı temsilci yoksa null.</summary>
    public Task<AgentPresenceItem?> ConnectPresenceAsync() => PostPresenceAsync("/agent/presence/connect");

    public Task<AgentPresenceItem?> HeartbeatPresenceAsync() => PostPresenceAsync("/agent/presence/heartbeat");

    public async Task<AgentPresenceItem?> SetPresenceAsync(string presence)
    {
        try
        {
            using var response = await http.PutAsJsonAsync("/agent/presence", new { presence });
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<AgentPresenceItem>() : null;
        }
        catch { return null; }
    }

    /// <summary>Yalnız yönetici: aktif temsilcilerin geçerli durumu.</summary>
    public async Task<List<AgentPresenceItem>> GetAgentPresenceAsync()
    {
        try { return await http.GetFromJsonAsync<List<AgentPresenceItem>>("/agents/presence") ?? []; }
        catch { return []; }
    }

    private async Task<AgentPresenceItem?> PostPresenceAsync(string url)
    {
        try
        {
            using var response = await http.PostAsync(url, null);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<AgentPresenceItem>() : null;
        }
        catch { return null; }
    }

    // ─── Hazır yanıtlar ──────────────────────────────────────────────────────

    /// <summary>Arama sunucuda Türkçe kurallarla yapılır (başlık/metin/kısayol). Temsilci /agent önekiyle okur.</summary>
    public async Task<List<SavedReplyItem>> GetSavedRepliesAsync(string? query = null)
    {
        var prefix = await PrefixAsync();
        var url = $"{prefix}/saved-replies" + (string.IsNullOrWhiteSpace(query) ? "" : $"?q={Uri.EscapeDataString(query.Trim())}");
        return await http.GetFromJsonAsync<List<SavedReplyItem>>(url) ?? [];
    }

    /// <summary>Ekler (<paramref name="id"/> null) ya da günceller — yalnız yönetici. Hata metni sunucudan gelir.</summary>
    public async Task<(SavedReplyItem? Reply, string? Error)> SaveSavedReplyAsync(string? id, string title, string body, string? shortcut)
    {
        var payload = new { title, body, shortcut = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut };
        var response = id is null
            ? await http.PostAsJsonAsync("/saved-replies", payload)
            : await http.PutAsJsonAsync($"/saved-replies/{Uri.EscapeDataString(id)}", payload);
        if (response.IsSuccessStatusCode) return (await response.Content.ReadFromJsonAsync<SavedReplyItem>(), null);
        try { return (null, (await response.Content.ReadFromJsonAsync<ApiError>())?.Message ?? "Kaydedilemedi."); }
        catch { return (null, "Kaydedilemedi."); }
    }

    public async Task<bool> DeleteSavedReplyAsync(string id) =>
        (await http.DeleteAsync($"/saved-replies/{Uri.EscapeDataString(id)}")).IsSuccessStatusCode;

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

    /// <summary>
    /// Onaylanmış ama yürütmesi askıda kalmış kayıtlar. "Son N" listesinden ayrı bir çağrıdır
    /// çünkü askıdaki bir kayıt trafik arttıkça o listeden düşer — tam da görünmesi gereken
    /// kayıt görünmez olurdu.
    /// </summary>
    public async Task<List<ApprovalRequest>> GetStuckApprovalsAsync()
    {
        var prefix = await PrefixAsync();
        var result = await http.GetFromJsonAsync<List<ApprovalRequest>>($"{prefix}/approvals/stuck");
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

    /// <summary>Temsilci asistanı — özet, bağlam ve yanıt taslağı (LLM çağrısı içerir; istek üzerine).</summary>
    public async Task<AgentAssistResult?> GetAgentAssistAsync(string sessionId)
    {
        var prefix = await PrefixAsync();
        var response = await http.GetAsync($"{prefix}/chat-sessions/{sessionId}/assist");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentAssistResult>();
    }

    public async Task TakeoverAsync(string sessionId, string humanAgent)
    {
        var prefix = await PrefixAsync();
        var response = await http.PostAsJsonAsync($"{prefix}/chat-sessions/{sessionId}/takeover",
            new { humanAgent });
        response.EnsureSuccessStatusCode();
    }

    // ─── Siparişler (yalnız yönetici) ────────────────────────────────────────

    public async Task<OrderViewItem?> GetOrderAsync(string orderId)
    {
        try
        {
            using var response = await http.GetAsync($"/orders/{Uri.EscapeDataString(orderId.Trim())}");
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<OrderViewItem>() : null;
        }
        catch { return null; }
    }

    /// <summary><c>Changed=false</c>: sipariş zaten bu durumdaydı (e-posta tekrar gitmez).</summary>
    public Task<(OrderViewItem? Order, bool Changed, string? Error)> ShipOrderAsync(string orderId, string? carrier, string? trackingNumber) =>
        UpdateOrderAsync($"/orders/{Uri.EscapeDataString(orderId)}/shipment", new { carrier, trackingNumber });

    public Task<(OrderViewItem? Order, bool Changed, string? Error)> DeliverOrderAsync(string orderId) =>
        UpdateOrderAsync($"/orders/{Uri.EscapeDataString(orderId)}/delivery", new { });

    private async Task<(OrderViewItem? Order, bool Changed, string? Error)> UpdateOrderAsync(string url, object body)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(url, body);
            if (response.IsSuccessStatusCode)
            {
                var ok = await response.Content.ReadFromJsonAsync<OrderUpdateResponse>();
                return (ok?.Order, ok?.Changed ?? false, null);
            }
            var error = await response.Content.ReadFromJsonAsync<OrderUpdateError>();
            var message = error?.Error ?? $"Sipariş güncellenemedi ({(int)response.StatusCode}).";
            return (null, false, error?.CurrentStatus is { } current ? $"{message} Mevcut durum: {current}." : message);
        }
        catch (Exception ex)
        {
            return (null, false, "Sipariş güncellenemedi: " + ex.Message);
        }
    }

    private sealed record OrderUpdateResponse(string OrderId, bool Changed, OrderViewItem? Order);
    private sealed record OrderUpdateError(string? Error, string? CurrentStatus);

    // ─── Konuşma arama (yalnız yönetici) ─────────────────────────────────────

    /// <summary><paramref name="fromUtc"/> dahil, <paramref name="toUtc"/> hariç.</summary>
    public async Task<(ConversationSearchPageItem? Page, string? Error)> SearchConversationsAsync(
        string? text, string? customerId, DateTime? fromUtc, DateTime? toUtc, string? reason, string? tag, int page)
    {
        var query = new List<string>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) query.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
        }
        Add("q", text);
        Add("customerId", customerId);
        Add("from", fromUtc?.ToString("o"));
        Add("to", toUtc?.ToString("o"));
        Add("reason", reason);
        Add("tag", tag);
        query.Add($"page={page}");
        try
        {
            using var response = await http.GetAsync("/conversations/search?" + string.Join("&", query));
            if (response.IsSuccessStatusCode) return (await response.Content.ReadFromJsonAsync<ConversationSearchPageItem>(), null);
            var error = await response.Content.ReadFromJsonAsync<SearchError>();
            return (null, error?.Error ?? $"Arama yapılamadı ({(int)response.StatusCode}).");
        }
        catch (Exception ex)
        {
            return (null, "Arama yapılamadı: " + ex.Message);
        }
    }

    private sealed record SearchError(string? Error);

    // ─── Konuşma kapanışı ───────────────────────────────────────────────────

    public async Task<ConversationClosingOptions?> GetClosingOptionsAsync()
    {
        try
        {
            var prefix = await PrefixAsync();
            return await http.GetFromJsonAsync<ConversationClosingOptions>($"{prefix}/conversation-closing/options");
        }
        catch { return null; }
    }

    /// <summary>
    /// Sohbeti kapanış nedeni/etiket/notla kapatır. <c>Error</c> doluysa sohbet kapanmadı (doğrulama ya da
    /// sohbet canlı değil); <c>Warning</c> doluysa kapandı ama kapanış kaydı yazılamadı.
    /// </summary>
    public async Task<(bool Closed, string? Error, string? Warning)> CloseChatAsync(
        string sessionId, string? reason, IReadOnlyList<string> tags, string? note)
    {
        try
        {
            var prefix = await PrefixAsync();
            using var response = await http.PostAsJsonAsync(
                $"{prefix}/chat-sessions/{Uri.EscapeDataString(sessionId)}/close", new { reason, tags, note });
            var body = await response.Content.ReadFromJsonAsync<CloseChatResponse>();
            return response.IsSuccessStatusCode
                ? (true, null, body?.Warning)
                : (false, body?.Error ?? $"Sohbet kapatılamadı ({(int)response.StatusCode}).", null);
        }
        catch (Exception ex)
        {
            return (false, "Sohbet kapatılamadı: " + ex.Message, null);
        }
    }

    private sealed record CloseChatResponse(string? Error, string? Warning);

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

    /// <summary>
    /// Tarama tetikler. <c>Candidates</c>/<c>Proposed</c> yalnızca <c>Error is null</c> iken
    /// anlamlıdır — hata durumunda tarama hiç çalışmamıştır ve sayaçlar <c>-1</c> döner.
    /// (Eskiden hata yolunda 0 dönüyordu ve arayüz bunu "0 aday bulundu" gibi gösteriyordu.)
    /// </summary>
    public async Task<(int Candidates, int Proposed, string? Error)> MineImprovementsAsync()
    {
        try
        {
            var resp = await http.PostAsync("/improvements/mine", null);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = (int)resp.StatusCode switch
                {
                    429 => "Çok fazla istek gönderildi (dakikalık sınır). Birkaç saniye sonra tekrar deneyin.",
                    401 or 403 => "Bu işlem için yetkiniz yok veya oturum süresi dolmuş.",
                    503 => "Servis şu anda kullanılamıyor.",
                    var code => $"Sunucu hatası (HTTP {code})."
                };
                return (-1, -1, msg);
            }
            var j = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var candidates = j.TryGetProperty("candidates",      out var c) ? c.GetInt32() : 0;
            var proposed   = j.TryGetProperty("proposedLessons", out var p) ? p.GetInt32() : 0;
            var error      = j.TryGetProperty("error",           out var e) && e.ValueKind != System.Text.Json.JsonValueKind.Null ? e.GetString() : null;
            return (candidates, proposed, error);
        }
        catch (Exception ex) { return (-1, -1, ex.Message); }
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
