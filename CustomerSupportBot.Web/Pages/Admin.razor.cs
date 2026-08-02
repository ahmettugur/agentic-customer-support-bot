using System.Text.Json;
using System.Security.Claims;
using CustomerSupportBot.Web.Models;
using CustomerSupportBot.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace CustomerSupportBot.Web.Pages;

public partial class Admin
{
    // ── State ──────────────────────────────────────────────────────────────────
    private string _activeTab   = "approvals";
    private bool   _autoRefresh = true;
    private Timer? _refreshTimer;
    private bool   _isAgent;
    private DotNetObjectReference<Admin>? _dotNetRef;

    // ── Sentiment & SSE ───────────────────────────────────────────────────────
    private string? _sentimentLabel;
    private double  _sentimentScore;
    private Timer?  _sentimentTimer;

    private List<ApprovalRequest>    _pendingApprovals   = [];
    private List<ApprovalRequest>    _historyApprovals   = [];
    private List<EscalationRequest>  _openEscalations    = [];
    private List<EscalationRequest>  _closedEscalations  = [];
    private List<EscalationRequest>  _historyEscalations = [];
    private List<ActiveChatSession>  _activeChats        = [];
    private ActiveChatSession?       _openChatSession;
    private List<ChatHistoryMessage> _chatMessages       = [];
    private string                   _chatInput          = string.Empty;
    private AnalyticsDashboard?      _analytics;
    private List<SessionSummary>     _analyticsSessions          = [];
    private string?                  _analyticsSelectedSessionId;
    private SessionAnalyticsModel?   _sessionAnalytics;
    private bool                     _showSessionAnalytics;
    private List<LessonProposal>     _proposedLessons    = [];
    private List<LessonProposal>     _approvedLessons    = [];
    private bool                     _miningInProgress;
    private string?                  _miningStats;
    private string?                  _errorMessage;

    // ── Prompt modal ───────────────────────────────────────────────────────────
    private enum PromptKind { None, ApproveApproval, RejectApproval, ResolveEscalation, DismissEscalation, ReplanEscalation, ReplanChat, ApproveLesson, RejectLesson, TakeoverChat }
    private PromptKind _promptKind;
    private string     _promptId    = string.Empty;
    private string     _promptTitle = string.Empty;
    private string     _promptLabel = string.Empty;
    private string     _promptInput = string.Empty;
    private bool       _showPromptModal;
    private bool       _promptRequired;

    // NOT: Burada eskiden yüksek riskli tool'ların yerel bir kopyası tutuluyordu ve bu kopya
    // sunucudaki WellKnown.HighRiskTools ile senkronunu kaybetmişti (order_cancel_tool ve
    // return_request_tool eksikti) — bu tool'lar için gerekçe "isteğe bağlı" gösteriliyor,
    // admin boş bırakınca backend 400 approval_reason_required dönüyordu. Karar artık
    // sunucuda veriliyor: ApprovalRequest.ReasonRequired.

    // ── Takeover pending state ──────────────────────────────────────────────────
    private EscalationRequest? _pendingTakeoverEsc;

    // ── Assign modal ───────────────────────────────────────────────────────────
    private bool             _showAssignModal;
    private string           _assignEscalationId = string.Empty;
    private List<AgentInfo>  _assignAgents       = [];
    private string           _assignSelected     = string.Empty;
    private string           _assignFallbackText = string.Empty;

    // ── Transcript modal ───────────────────────────────────────────────────────
    private bool                     _showTranscriptModal;
    private string                   _transcriptSubtitle = string.Empty;
    private List<ChatHistoryMessage>  _transcriptMessages = [];

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _isAgent = state.User.FindFirst(ClaimTypes.Role)?.Value == "Agent";
        if (_isAgent) _activeTab = "escalations";

        await RefreshBadgesAsync();
        await RefreshActiveTabAsync();
        if (_autoRefresh) StartAutoRefresh();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        var token   = await TokenStore.GetAccessTokenAsync() ?? "";
        var apiBase = Http.BaseAddress?.ToString().TrimEnd('/') ?? "";
        await JS.InvokeVoidAsync("loadScript", "/js/admin-chat-bridge.js", "js-admin-chat-bridge");
        await JS.InvokeVoidAsync("__adminChatSetup", token, apiBase);
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────
    private string ActiveCls(string tab) => _activeTab == tab ? "active" : string.Empty;

    private async Task SwitchTabAsync(string tab)
    {
        _activeTab = tab;
        await RefreshActiveTabAsync();
    }

    // ── Refresh ───────────────────────────────────────────────────────────────
    private async Task RefreshBadgesAsync()
    {
        try
        {
            var approvals   = AdminApi.GetPendingApprovalsAsync();
            var escalations = AdminApi.GetOpenEscalationsAsync();
            var chats       = AdminApi.GetActiveChatsAsync();
            await Task.WhenAll(approvals, escalations, chats);
            _pendingApprovals = approvals.Result;
            _openEscalations  = escalations.Result;
            _activeChats      = chats.Result;
        }
        catch { /* badge hatalarını sessizce geç */ }
    }

    private async Task RefreshActiveTabAsync()
    {
        try
        {
            switch (_activeTab)
            {
                case "approvals":
                    _pendingApprovals = await AdminApi.GetPendingApprovalsAsync();
                    break;
                case "escalations":
                    _openEscalations   = await AdminApi.GetOpenEscalationsAsync();
                    var recent         = await AdminApi.GetRecentEscalationsAsync(30);
                    _closedEscalations = recent.Where(e => e.Status is "resolved" or "dismissed").ToList();
                    break;
                case "chats":
                    _activeChats = await AdminApi.GetActiveChatsAsync();
                    if (_openChatSession is not null)
                        _chatMessages = await AdminApi.GetChatHistoryAsync(_openChatSession.SessionId);
                    break;
                case "analytics":
                    var sessTask  = AdminApi.GetSessionsAsync();
                    var dashTask  = AnalyticsApi.GetDashboardAsync();
                    await Task.WhenAll(sessTask, dashTask);
                    _analyticsSessions = sessTask.Result;
                    _analytics         = dashTask.Result;
                    if (_showSessionAnalytics && _analyticsSelectedSessionId is not null)
                        _sessionAnalytics = await AnalyticsApi.GetSessionAnalyticsAsync(_analyticsSelectedSessionId);
                    break;
                case "history":
                    _historyApprovals   = await AdminApi.GetRecentApprovalsAsync(50);
                    _historyEscalations = await AdminApi.GetRecentEscalationsAsync(50);
                    break;
                case "improvements":
                    _proposedLessons = await AdminApi.GetLessonsAsync("Proposed");
                    _approvedLessons = await AdminApi.GetLessonsAsync("Approved");
                    break;
            }
            _errorMessage = null;
        }
        catch (HttpRequestException ex)
        {
            _errorMessage = ex.StatusCode.HasValue
                ? $"API hatası ({(int)ex.StatusCode.Value}): {ex.Message}"
                : $"API'ye bağlanılamadı: {ex.Message}";
        }
        catch (Exception ex)
        {
            _errorMessage = $"Beklenmeyen hata: {ex.Message}";
        }
    }

    // ── Prompt modal ──────────────────────────────────────────────────────────
    private void ShowPrompt(PromptKind kind, string id, string title, string label, bool required = false)
    {
        _promptKind     = kind;
        _promptId       = id;
        _promptTitle    = title;
        _promptLabel    = label;
        _promptInput    = string.Empty;
        _promptRequired = required;
        _showPromptModal = true;
    }

    private void ShowApprovePrompt(ApprovalRequest a)
    {
        var required = a.ReasonRequired;
        var label = required
            ? "Onay gerekçesi (zorunlu — yüksek riskli işlem)"
            : "Onay notu (isteğe bağlı)";
        ShowPrompt(PromptKind.ApproveApproval, a.Id, "Onayla", label, required);
    }

    private void CancelPrompt() { _showPromptModal = false; }

    private void OnModalKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _showPromptModal     = false;
            _showAssignModal     = false;
            _showTranscriptModal = false;
        }
    }

    private async Task ConfirmPromptAsync()
    {
        _showPromptModal = false;
        var v = _promptInput;
        _promptInput = string.Empty;
        try
        {
            switch (_promptKind)
            {
                case PromptKind.ApproveApproval:
                    await AdminApi.ApproveAsync(_promptId, v);
                    Toast.ShowSuccess("Tool çağrısı onaylandı.");
                    break;
                case PromptKind.RejectApproval:
                    await AdminApi.RejectAsync(_promptId, v);
                    Toast.ShowInfo("Tool çağrısı reddedildi.");
                    break;
                case PromptKind.ResolveEscalation:
                    await AdminApi.ResolveEscalationAsync(_promptId, v);
                    Toast.ShowSuccess("Eskalasyon çözüldü.");
                    break;
                case PromptKind.DismissEscalation:
                    await AdminApi.DismissAsync(_promptId, v);
                    Toast.ShowInfo("Eskalasyon dismiss edildi.");
                    break;
                case PromptKind.ReplanEscalation:
                    await AdminApi.ReplanEscalationAsync(_promptId, v);
                    if (_openChatSession is not null) { StopChatPanel(); _openChatSession = null; _chatMessages = []; _sentimentLabel = null; }
                    Toast.ShowSuccess("Yeniden planlama başlatıldı.");
                    break;
                case PromptKind.ReplanChat:
                    await AdminApi.ReplanChatAsync(_promptId, v);
                    StopChatPanel(); _openChatSession = null; _chatMessages = []; _sentimentLabel = null;
                    Toast.ShowSuccess("Sohbet yeniden planlandı.");
                    break;
                case PromptKind.TakeoverChat:
                    if (_pendingTakeoverEsc is not null)
                    {
                        var esc = _pendingTakeoverEsc;
                        _pendingTakeoverEsc = null;
                        await DoTakeoverAsync(esc, string.IsNullOrWhiteSpace(v) ? null : v);
                    }
                    break;
                case PromptKind.ApproveLesson:
                    await AdminApi.ApproveLessonAsync(_promptId, v);
                    Toast.ShowSuccess("Ders önerisi onaylandı.");
                    break;
                case PromptKind.RejectLesson:
                    await AdminApi.RejectLessonAsync(_promptId, v);
                    Toast.ShowInfo("Ders önerisi reddedildi.");
                    break;
            }
        }
        catch (HttpRequestException ex)
        {
            var msg = ex.StatusCode.HasValue
                ? $"İşlem başarısız (HTTP {(int)ex.StatusCode.Value}): {ex.Message}"
                : $"API'ye bağlanılamadı: {ex.Message}";
            _errorMessage = msg;
            Toast.ShowError(msg);
        }
        catch (Exception ex)
        {
            _errorMessage = $"Beklenmeyen hata: {ex.Message}";
            Toast.ShowError(_errorMessage);
        }
        await RefreshActiveTabAsync();
    }

    // ── Escalation takeover ────────────────────────────────────────────────────
    private async Task TakeoverAndChatAsync(EscalationRequest e)
    {
        if (e.SessionId is null) return;
        _errorMessage = null;

        if (_isAgent)
        {
            await DoTakeoverAsync(e, agentName: null);
        }
        else
        {
            _pendingTakeoverEsc = e;
            ShowPrompt(PromptKind.TakeoverChat, e.SessionId, "Sohbeti Devral", "Temsilci adınız");
        }
    }

    private async Task DoTakeoverAsync(EscalationRequest e, string? agentName)
    {
        if (e.SessionId is null) return;
        try
        {
            await AdminApi.TakeoverAsync(e.SessionId, agentName ?? "admin");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            _errorMessage = "Bu oturum zaten başka bir temsilci tarafından devralınmış. Listeyi yenileyiniz.";
            Toast.ShowError(_errorMessage);
            await RefreshActiveTabAsync();
            return;
        }
        _activeChats = await AdminApi.GetActiveChatsAsync();
        var session  = _activeChats.FirstOrDefault(s => s.SessionId == e.SessionId)
                       ?? new ActiveChatSession(e.SessionId, agentName ?? "admin", 0, null, null, DateTimeOffset.UtcNow);
        await OpenChatPanelAsync(session);
        await SwitchTabAsync("chats");
    }

    // ── Chat panel ─────────────────────────────────────────────────────────────
    private async Task OpenChatPanelAsync(ActiveChatSession s)
    {
        StopChatPanel();
        _openChatSession = s;
        _chatMessages    = await AdminApi.GetChatHistoryAsync(s.SessionId);
        try { await JS.InvokeVoidAsync("__adminSubscribeChat", _dotNetRef, s.SessionId); } catch { }
        await RefreshSentimentAsync();
        StartSentimentTimer(s.SessionId);
    }

    private async Task SendChatMessageAsync()
    {
        if (_openChatSession is null || string.IsNullOrWhiteSpace(_chatInput)) return;
        var text = _chatInput.Trim();
        _chatInput = string.Empty;
        await AdminApi.SendChatMessageAsync(_openChatSession.SessionId, text);
        _chatMessages.Add(new ChatHistoryMessage("Admin", text, DateTimeOffset.UtcNow));
        await ScrollChatToBottomAsync();
    }

    private async Task ReleaseChatAsync()
    {
        if (_openChatSession is null) return;
        await AdminApi.ReleaseAsync(_openChatSession.SessionId);
        StopChatPanel();
        _openChatSession = null;
        _chatMessages    = [];
        _sentimentLabel  = null;
        _activeChats     = await AdminApi.GetActiveChatsAsync();
        Toast.ShowInfo("Sohbet sonlandırıldı.");
    }

    private void StopChatPanel()
    {
        _sentimentTimer?.Dispose();
        _sentimentTimer = null;
        _ = JS.InvokeVoidAsync("__adminStopChat").AsTask()
              .ContinueWith(t => { if (t.IsFaulted) Console.Error.WriteLine($"[StopChat] {t.Exception}"); },
                            TaskContinuationOptions.OnlyOnFaulted);
    }

    // ── SSE bridge_message callback ───────────────────────────────────────────
    [JSInvokable]
    public async Task OnChatEvent(string type, string data)
    {
        if (type != "bridge_message" || _openChatSession is null) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            var root = doc.RootElement;
            var sender  = root.TryGetProperty("sender",    out var s) ? s.GetString() ?? "user" : "user";
            var text    = root.TryGetProperty("text",      out var t) ? t.GetString() ?? "" : "";
            var tsRaw   = root.TryGetProperty("timestamp", out var ts) ? ts.GetString() : null;
            var stamp   = DateTimeOffset.TryParse(tsRaw, out var dt) ? dt : DateTimeOffset.UtcNow;
            await InvokeAsync(() =>
            {
                _chatMessages.Add(new ChatHistoryMessage(sender, text, stamp));
                StateHasChanged();
                _ = ScrollChatToBottomAsync().ContinueWith(t =>
                    { if (t.IsFaulted) Console.Error.WriteLine($"[ScrollChat] {t.Exception}"); },
                    TaskContinuationOptions.OnlyOnFaulted);
            });
        }
        catch { }
    }

    // ── Sentiment ─────────────────────────────────────────────────────────────
    private void StartSentimentTimer(string sessionId)
    {
        _sentimentTimer = new Timer(async _ =>
        {
            try
            {
                await RefreshSentimentAsync();
                await InvokeAsync(StateHasChanged);
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (Exception ex) { Console.Error.WriteLine($"[SentimentTimer] {ex}"); }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private async Task RefreshSentimentAsync()
    {
        if (_openChatSession is null) return;
        var s = await AdminApi.GetSentimentAsync(_openChatSession.SessionId);
        if (s is null) return;
        _sentimentLabel = s.Sentiment;
        _sentimentScore = s.Score;
    }

    private async Task ScrollChatToBottomAsync()
    {
        try { await JS.InvokeVoidAsync("__adminScrollToBottom", "adminChatMessages"); }
        catch { }
    }

    // ── Assign modal ──────────────────────────────────────────────────────────
    private async Task ShowAssignModalAsync(string escalationId)
    {
        _assignEscalationId = escalationId;
        _assignAgents       = await AdminApi.GetAgentsAsync();
        _assignSelected     = _assignAgents.FirstOrDefault(a => a.IsActive)?.Id ?? string.Empty;
        _assignFallbackText = string.Empty;
        _showAssignModal    = true;
    }

    private async Task ConfirmAssignAsync()
    {
        _showAssignModal = false;
        var assignedTo = _assignAgents.Count > 0 ? _assignSelected : _assignFallbackText.Trim();
        if (string.IsNullOrWhiteSpace(assignedTo)) return;
        await AdminApi.AcknowledgeAsync(_assignEscalationId, assignedTo);
        Toast.ShowSuccess($"Eskalasyon '{assignedTo}' temsilcisine atandı.");
        await RefreshActiveTabAsync();
    }

    // ── Transcript modal ───────────────────────────────────────────────────────
    private async Task ShowTranscriptAsync(string? sessionId)
    {
        if (sessionId is null) return;
        _transcriptSubtitle  = $"Session: {sessionId[..Math.Min(8, sessionId.Length)]}…";
        _transcriptMessages  = await AdminApi.GetChatHistoryAsync(sessionId);
        _showTranscriptModal = true;
    }

    // ── Improvements ──────────────────────────────────────────────────────────
    private async Task MineImprovementsAsync()
    {
        _miningInProgress = true;
        _miningStats      = null;
        var (candidates, proposed, error) = await AdminApi.MineImprovementsAsync();
        _miningStats      = $"Son tarama: {candidates} aday → {proposed} yeni öneri{(error is not null ? " · HATA: " + error : "")}";
        _miningInProgress = false;
        _proposedLessons  = await AdminApi.GetLessonsAsync("Proposed");
    }

    // ── Analytics non-render helpers ──────────────────────────────────────────
    private void ShowAggregateAnalytics()
    {
        _analyticsSelectedSessionId = null;
        _showSessionAnalytics       = false;
        _sessionAnalytics           = null;
    }

    private async Task SelectAnalyticsSessionAsync(string sid)
    {
        _analyticsSelectedSessionId = sid;
        _showSessionAnalytics       = true;
        _sessionAnalytics           = null;
        StateHasChanged();
        _sessionAnalytics = await AnalyticsApi.GetSessionAnalyticsAsync(sid);
    }

    // ── Timer ─────────────────────────────────────────────────────────────────
    private void StartAutoRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = new Timer(async _ =>
        {
            try
            {
                await InvokeAsync(async () =>
                {
                    await RefreshBadgesAsync();
                    await RefreshActiveTabAsync();
                    StateHasChanged();
                });
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (Exception ex) { Console.Error.WriteLine($"[AutoRefresh] {ex}"); }
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    private void OnAutoRefreshChanged()
    {
        if (_autoRefresh) StartAutoRefresh();
        else { _refreshTimer?.Dispose(); _refreshTimer = null; }
    }

    public async ValueTask DisposeAsync()
    {
        StopChatPanel();
        _dotNetRef?.Dispose();
        if (_refreshTimer is not null) await _refreshTimer.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string ShortId(string? id) =>
        id is null ? "—" : id[..Math.Min(8, id.Length)] + "…";

    /// <summary>
    /// Onay kartındaki tool argümanlarını (ApprovalRequest.Parameters) alan-alan gösterilebilir
    /// hale getirir. Sunucu <c>Dictionary&lt;string, object?&gt;</c> gönderiyor; DTO'da
    /// <c>object?</c> olduğu için istemcide <see cref="JsonElement"/> olarak çözülür.
    /// Nesne değilse veya boşsa null döner (kart o bölümü hiç çizmez).
    /// Replay.razor'daki aynı isimli yardımcının string yerine JsonElement alan karşılığı.
    /// </summary>
    private static List<(string Key, string Value)>? ParseJsonFields(object? parameters)
    {
        if (parameters is not JsonElement root || root.ValueKind != JsonValueKind.Object)
            return null;

        var list = new List<(string, string)>();
        foreach (var prop in root.EnumerateObject())
        {
            var val = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.True   => "true",
                JsonValueKind.False  => "false",
                JsonValueKind.Null   => "—",
                _                    => prop.Value.GetRawText()
            };
            list.Add((prop.Name, val));
        }
        return list.Count > 0 ? list : null;
    }

    private static string FmtRelative(DateTimeOffset dt)
    {
        var d = DateTimeOffset.UtcNow - dt;
        if (d.TotalSeconds < 60)  return $"{(int)d.TotalSeconds}s önce";
        if (d.TotalMinutes < 60)  return $"{(int)d.TotalMinutes}dk önce";
        if (d.TotalHours   < 24)  return $"{(int)d.TotalHours}sa önce";
        return dt.LocalDateTime.ToShortDateString();
    }

    private static string FmtTs(DateTimeOffset dt) => dt.LocalDateTime.ToString("HH:mm:ss");

    private static string FmtRelative(DateTime dt)
    {
        var d = DateTime.UtcNow - dt.ToUniversalTime();
        if (d.TotalSeconds < 60)  return $"{(int)d.TotalSeconds}s önce";
        if (d.TotalMinutes < 60)  return $"{(int)d.TotalMinutes}dk önce";
        if (d.TotalHours   < 24)  return $"{(int)d.TotalHours}sa önce";
        return dt.ToLocalTime().ToShortDateString();
    }
}
