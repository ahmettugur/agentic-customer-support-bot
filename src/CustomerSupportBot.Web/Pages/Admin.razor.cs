using System.Text.Json;
using System.Security.Claims;
using CustomerSupportBot.Web.Helpers;
using CustomerSupportBot.Web.Models;
using CustomerSupportBot.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace CustomerSupportBot.Web.Pages;

public partial class Admin
{
    // ── State ──────────────────────────────────────────────────────────────────
    // Bölüm adresten gelir (/admin?tab=…) — kenar çubuğu linkleri, yenileme ve geri tuşu aynı bölümde kalır.
    [SupplyParameterFromQuery(Name = "tab")] public string? Tab { get; set; }
    private bool   _initialized;
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
    private List<ApprovalRequest>    _stuckApprovals     = [];
    private List<EscalationRequest>  _openEscalations    = [];
    private List<EscalationRequest>  _closedEscalations  = [];
    private List<EscalationRequest>  _historyEscalations = [];
    private List<ActiveChatSession>  _activeChats        = [];
    private ActiveChatSession?       _openChatSession;
    private List<ChatHistoryMessage> _chatMessages       = [];
    private string                   _chatInput          = string.Empty;

    // ── Temsilci asistanı (canlı sohbet paneli) ─────────────────────────────────
    // İstek üzerine yüklenir — her panel açılışında LLM çağrısı yapılmaz.
    private bool               _assistOpen;
    private bool               _assistLoading;
    private AgentAssistResult? _assist;
    private string?            _assistLoadError;
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
    private enum PromptKind { None, ResolveEscalation, DismissEscalation, ReplanEscalation, ReplanChat, ApproveLesson, RejectLesson, TakeoverChat }
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

    // ── Onay kararı sürerken / verildikten sonra ────────────────────────────────
    // Karar isteği sürerken kartın düğmeleri pasif (çift tıklama ikinci istek göndermesin). Karar verilen kart
    // listeden ANINDA kalkar: eskiden kart bir sonraki otomatik yenilemeye (≤15 sn) kadar düğmeleri açık
    // kalıyordu — Onay Kuyruğu sekmesinde karar sonrası yenileme listeyi yeniden çekmiyordu. Karar verilmiş
    // kayıtlar, karar anında sürmekte olan bir yenilemenin eski listesiyle geri gelmesin diye süzülür.
    private readonly HashSet<string> _decidingApprovals = [];
    private readonly HashSet<string> _decidedApprovals  = [];

    // ── Onay listesi + detay ──────────────────────────────────────────────────
    // Liste en uzun bekleyen en üstte; sağda seçili talebin ayrıntısı ve karar alanı (eski modal yerine).
    private string? _selectedApprovalId;
    private string  _approvalNote   = string.Empty;
    private string  _approvalFilter = "all";

    /// <summary>Bu süreden uzun bekleyen talep listede vurgulanır.</summary>
    private static readonly TimeSpan LongWait = TimeSpan.FromMinutes(15);

    private IEnumerable<ApprovalRequest> SortedApprovals => _pendingApprovals.OrderBy(a => a.RequestedAt);

    private bool MatchesApprovalFilter(ApprovalRequest a, string filter) => filter switch
    {
        "all"  => true,
        "risk" => a.ReasonRequired,
        _      => a.ToolName == filter
    };

    private List<ApprovalRequest> VisibleApprovals
        => SortedApprovals.Where(a => MatchesApprovalFilter(a, _approvalFilter)).ToList();

    private ApprovalRequest? SelectedApproval(List<ApprovalRequest> visible)
        => visible.FirstOrDefault(a => a.Id == _selectedApprovalId) ?? visible.FirstOrDefault();

    /// <summary>Filtre çipleri: yalnızca kuyrukta olan türler (+ varsa yüksek risk).</summary>
    private IEnumerable<(string Key, string Label, int Count)> ApprovalFilters()
    {
        yield return ("all", "Tümü", _pendingApprovals.Count);
        foreach (var g in _pendingApprovals.GroupBy(a => a.ToolName).OrderBy(g => ToolDisplayName(g.Key)))
            yield return (g.Key, ToolDisplayName(g.Key), g.Count());
        var risky = _pendingApprovals.Count(a => a.ReasonRequired);
        if (risky > 0) yield return ("risk", "Yüksek risk", risky);
    }

    private void SelectApproval(ApprovalRequest a)
    {
        if (_selectedApprovalId == a.Id) return;
        _selectedApprovalId = a.Id;
        _approvalNote       = string.Empty;
    }

    private void SetApprovalFilter(string key)
    {
        _approvalFilter     = key;
        _selectedApprovalId = null;
        _approvalNote       = string.Empty;
    }

    private static bool IsLongWait(ApprovalRequest a) => DateTimeOffset.UtcNow - a.RequestedAt >= LongWait;

    // ── Eskalasyon kartı "diğer işlemler" menüsü ────────────────────────────────
    private string? _escMenuOpenId;
    private void ToggleEscMenu(string id) => _escMenuOpenId = _escMenuOpenId == id ? null : id;
    private void CloseEscMenu() => _escMenuOpenId = null;

    // ── Takeover pending state ──────────────────────────────────────────────────
    private EscalationRequest? _pendingTakeoverEsc;

    // ── Assign modal ───────────────────────────────────────────────────────────
    private bool             _showAssignModal;
    private string           _assignEscalationId = string.Empty;
    private List<AgentInfo>  _assignAgents       = [];
    private string           _assignSelected     = string.Empty;
    private string           _assignFallbackText = string.Empty;

    // ── Temsilci durumu ────────────────────────────────────────────────────────
    // Temsilci: kendi durumu + 30 sn'lik kalp atışı (sunucu 90 sn'de çevrimdışı sayar).
    // Yönetici: Eskalasyonlar sekmesinde ve atama penceresinde herkesin durumu.
    private static readonly TimeSpan PresenceHeartbeatInterval = TimeSpan.FromSeconds(30);
    private AgentPresenceItem?      _myPresence;
    private Timer?                  _presenceTimer;
    private List<AgentPresenceItem> _agentPresence = [];

    // ── Siparişler (yalnız yönetici) ─────────────────────────────────────────────
    private string         _orderLookupId = string.Empty;
    private OrderViewItem? _orderView;
    private string?        _orderError;
    private string         _orderCarrier  = string.Empty;
    private string         _orderTracking = string.Empty;
    private bool           _orderBusy;

    // ── Konuşma arama (yalnız yönetici) ──────────────────────────────────────────
    private string                          _searchText     = string.Empty;
    private string                          _searchCustomer = string.Empty;
    private DateOnly?                       _searchFrom;
    private DateOnly?                       _searchTo;
    private string                          _searchReason   = string.Empty;
    private string                          _searchTag      = string.Empty;
    private List<ConversationSearchHitItem> _searchResults  = [];
    private int                             _searchPage     = 1;
    private bool                            _searchHasMore;
    private bool                            _searching;
    private bool                            _searchedOnce;
    private string?                         _searchError;
    private ClosingReasonItem[]             _searchReasons  = [];

    // ── Sohbeti bitir (kapanış nedeni / etiketler / not) ───────────────────────
    private bool                        _showCloseModal;
    private bool                        _closing;
    private ConversationClosingOptions? _closeOptions;
    private string                      _closeReason = string.Empty;
    private string                      _closeTags   = string.Empty;
    private string                      _closeNote   = string.Empty;
    private string?                     _closeError;

    // ── Transcript modal ───────────────────────────────────────────────────────
    private bool                     _showTranscriptModal;
    private string                   _transcriptSubtitle = string.Empty;
    private List<ChatHistoryMessage>  _transcriptMessages = [];

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _isAgent = state.User.FindFirst(ClaimTypes.Role)?.Value == "Agent";
        _activeTab = AdminTabs.Normalize(Tab, _isAgent);
        if (_isAgent) await ConnectPresenceAsync();

        await RefreshBadgesAsync();
        await RefreshActiveTabAsync();
        if (_autoRefresh) StartAutoRefresh();
        _initialized = true;
    }

    // Kenar çubuğundan (ya da geri tuşuyla) aynı sayfada başka bölüme geçildi: sayfa yeniden oluşmaz,
    // yalnızca bölüm değişir — açık sohbet paneli ve canlı bağlantılar korunur.
    protected override async Task OnParametersSetAsync()
    {
        if (!_initialized) return;
        var tab = AdminTabs.Normalize(Tab, _isAgent);
        if (tab != _activeTab) await ActivateTabAsync(tab);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        var token   = await TokenStore.GetAccessTokenAsync(AuthScope.Staff) ?? "";
        var apiBase = Http.BaseAddress?.ToString().TrimEnd('/') ?? "";
        await JS.InvokeVoidAsync("loadScript", "/js/admin-chat-bridge.js", "js-admin-chat-bridge");
        await JS.InvokeVoidAsync("__adminChatSetup", token, apiBase);
    }

    // ── Tabs ──────────────────────────────────────────────────────────────────
    private string ActiveCls(string tab) => _activeTab == tab ? "active" : string.Empty;

    /// <summary>Sayfa içinden bölüm değiştirir ve adresi günceller (ör. devraldıktan sonra sohbetlere geç).</summary>
    private async Task SwitchTabAsync(string tab)
    {
        tab = AdminTabs.Normalize(tab, _isAgent);
        var fromUrl = AdminTabs.Normalize(Tab, _isAgent);
        // Önce bölüm atanır: adres değişince gelen OnParametersSetAsync aynı bölümü ikinci kez yüklemesin.
        _activeTab = tab;
        if (tab != fromUrl)
            Nav.NavigateTo($"/admin?tab={tab}");
        await ActivateTabAsync(tab);
    }

    private async Task ActivateTabAsync(string tab)
    {
        _activeTab = tab;
        _escMenuOpenId = null;
        // Sekme değişimi ELLE yapılan, seyrek bir işlem — burada rozet verisini de tazelemek
        // kota açısından önemsiz. Buna karşılık RefreshActiveTabAsync artık rozetlerle
        // çakışan istekleri tekrarlamıyor, dolayısıyla elle geçişte veri bayat kalmasın diye
        // bu çağrı gerekli.
        await RefreshBadgesAsync();
        await RefreshActiveTabAsync();
    }

    // ── Refresh ───────────────────────────────────────────────────────────────
    /// <summary>Başlıktaki "Şimdi yenile": kuyruk sayıları + açık bölüm.</summary>
    private async Task RefreshNowAsync()
    {
        await RefreshBadgesAsync();
        await RefreshActiveTabAsync();
    }

    private async Task RefreshBadgesAsync()
    {
        try
        {
            var approvals   = AdminApi.GetPendingApprovalsAsync();
            var escalations = AdminApi.GetOpenEscalationsAsync();
            var chats       = AdminApi.GetActiveChatsAsync();
            await Task.WhenAll(approvals, escalations, chats);
            _pendingApprovals = approvals.Result.Where(a => !_decidedApprovals.Contains(a.Id)).ToList();
            _openEscalations  = escalations.Result;
            _activeChats      = chats.Result;
            PublishBadges();
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
                    // RefreshBadgesAsync bekleyen onayları zaten çekti — otomatik yenilemede
                    // aynı turda ikinci kez istemek boşa kota harcıyordu (bkz. AutoRefreshInterval).
                    break;
                case "escalations":
                    // _openEscalations da RefreshBadgesAsync'ten geliyor; burada yalnızca
                    // yalnızca bu sekmeye özel olan kapanmış liste çekilir.
                    var recent         = await AdminApi.GetRecentEscalationsAsync(30);
                    _closedEscalations = recent.Where(e => e.Status is "resolved" or "dismissed").ToList();
                    if (!_isAgent) _agentPresence = await AdminApi.GetAgentPresenceAsync();
                    break;
                case "chats":
                    _activeChats = await AdminApi.GetActiveChatsAsync();
                    PublishBadges();
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
                    // Ayrı çağrı: askıda kalmış yürütmelerin "son 50" penceresinden düşmemesi gerekir.
                    _stuckApprovals     = await AdminApi.GetStuckApprovalsAsync();
                    break;
                case "replies":
                    _savedReplies = await AdminApi.GetSavedRepliesAsync();
                    break;
                case "conversations":
                    // Otomatik yenilemede arama tekrarlanmaz: sonuçlar ve "daha fazla" sayfaları sıfırlanırdı.
                    if (!_searchedOnce)
                    {
                        _searchReasons = (await AdminApi.GetClosingOptionsAsync())?.Reasons ?? [];
                        await SearchConversationsAsync(append: false);
                    }
                    break;
                case "improvements":
                    _proposedLessons = await AdminApi.GetLessonsAsync("Proposed");
                    Badges.SetProposedLessons(_proposedLessons.Count);
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

    private void CancelPrompt() { _showPromptModal = false; }

    private void OnModalKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _showPromptModal     = false;
            _showAssignModal     = false;
            _showTranscriptModal = false;
            if (!_closing) _showCloseModal = false;
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

    // ── Onay kararı (detay panelinde, modal yok) ───────────────────────────────
    private async Task DecideApprovalAsync(ApprovalRequest a, bool approve)
    {
        // Aynı kayda ikinci karar isteği gönderilmez (hızlı çift tıklama).
        if (!_decidingApprovals.Add(a.Id)) return;
        var note = _approvalNote;
        // Bildirimde işlemin adı ("Sipariş İptali onaylandı") — liste değişmeden önce alınır.
        var decided = ApprovalSubject(a.Id);
        try
        {
            if (approve)
            {
                await AdminApi.ApproveAsync(a.Id, note);
                RemoveDecidedApproval(a.Id);
                Toast.ShowSuccess($"{decided} onaylandı.");
            }
            else
            {
                await AdminApi.RejectAsync(a.Id, note);
                RemoveDecidedApproval(a.Id);
                Toast.ShowInfo($"{decided} reddedildi.");
            }
            if (_selectedApprovalId == a.Id) { _selectedApprovalId = null; _approvalNote = string.Empty; }
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
        finally
        {
            // Hata olduysa düğmeler tekrar açılır (karar verilmedi).
            _decidingApprovals.Remove(a.Id);
        }
    }

    private void RemoveDecidedApproval(string approvalId)
    {
        _decidedApprovals.Add(approvalId);
        _pendingApprovals = _pendingApprovals.Where(a => a.Id != approvalId).ToList();
        PublishBadges();
    }

    private void PublishBadges()
        => Badges.SetQueues(_pendingApprovals.Count, _openEscalations.Count, _activeChats.Count);

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
        ResetAssist();
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

    /// <summary>"Sohbeti Bitir": kapanış penceresini açar — sohbet ancak pencere onaylanınca kapanır.</summary>
    private async Task ReleaseChatAsync()
    {
        if (_openChatSession is null) return;
        _closeOptions = await AdminApi.GetClosingOptionsAsync();
        _closeReason  = string.Empty;
        _closeTags    = string.Empty;
        _closeNote    = string.Empty;
        _closeError   = _closeOptions is null ? "Kapanış seçenekleri yüklenemedi; neden seçmeden kapatmayı deneyebilirsiniz." : null;
        _showCloseModal = true;
    }

    private static List<string> SplitTags(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void AddSuggestedTag(string tag)
    {
        var tags = SplitTags(_closeTags);
        if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return;
        tags.Add(tag);
        _closeTags = string.Join(", ", tags);
    }

    private async Task ConfirmCloseAsync()
    {
        if (_openChatSession is null || _closing) return;
        _closing    = true;
        _closeError = null;
        try
        {
            var (closed, error, warning) = await AdminApi.CloseChatAsync(_openChatSession.SessionId,
                string.IsNullOrWhiteSpace(_closeReason) ? null : _closeReason, SplitTags(_closeTags),
                string.IsNullOrWhiteSpace(_closeNote) ? null : _closeNote);
            if (!closed)
            {
                _closeError = error;   // pencere açık kalır, sohbet kapanmadı
                return;
            }
            _showCloseModal = false;
            await AfterChatClosedAsync();
            if (warning is not null) Toast.ShowError(warning);
            else Toast.ShowInfo("Sohbet sonlandırıldı.");
        }
        finally { _closing = false; }
    }

    private async Task AfterChatClosedAsync()
    {
        StopChatPanel();
        _openChatSession = null;
        ResetAssist();
        _chatMessages    = [];
        _sentimentLabel  = null;
        _activeChats     = await AdminApi.GetActiveChatsAsync();
    }

    // ── Temsilci asistanı ──────────────────────────────────────────────────────
    private async Task ToggleAssistAsync()
    {
        _assistOpen = !_assistOpen;
        if (_assistOpen && _assist is null && !_assistLoading)
            await LoadAssistAsync();
    }

    private async Task LoadAssistAsync()
    {
        if (_openChatSession is null) return;
        var sessionId = _openChatSession.SessionId;
        _assistLoading   = true;
        _assistLoadError = null;
        StateHasChanged();
        try
        {
            var result = await AdminApi.GetAgentAssistAsync(sessionId);
            if (_openChatSession?.SessionId != sessionId) return;   // bu arada başka sohbete geçildi
            _assist = result;
            if (result is null) _assistLoadError = "Oturum bulunamadı.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AgentAssist] {ex}");
            _assistLoadError = "Asistan yüklenemedi; tekrar deneyin.";
        }
        finally
        {
            _assistLoading = false;
        }
    }

    /// <summary>Taslağı mesaj kutusuna alır — GÖNDERMEZ; temsilci düzenleyip kendisi gönderir.</summary>
    // ── Hazır yanıtlar ────────────────────────────────────────────────────────
    private bool                 _repliesOpen;
    private string               _replyQuery = string.Empty;
    private List<SavedReplyItem> _replyResults = [];
    private CancellationTokenSource? _replySearchCts;

    private async Task ToggleRepliesAsync()
    {
        _repliesOpen = !_repliesOpen;
        if (!_repliesOpen) return;
        _replyQuery = string.Empty;
        await SearchRepliesAsync();
    }

    /// <summary>Yazarken her tuşta istek atılmaz: 250 ms durulunca aranır, önceki arama iptal edilir.</summary>
    private async Task OnReplyQueryChangedAsync()
    {
        _replySearchCts?.Cancel();
        var cts = _replySearchCts = new CancellationTokenSource();
        try { await Task.Delay(250, cts.Token); }
        catch (TaskCanceledException) { return; }
        await SearchRepliesAsync();
    }

    private async Task SearchRepliesAsync()
    {
        try { _replyResults = await AdminApi.GetSavedRepliesAsync(_replyQuery); }
        catch { _replyResults = []; }
        StateHasChanged();
    }

    /// <summary>Mesaj kutusu boşsa yanıtla doldurulur, doluysa sonuna eklenir — göndermeden önce düzenlenebilir.</summary>
    private void InsertReply(SavedReplyItem reply)
    {
        _chatInput = string.IsNullOrWhiteSpace(_chatInput) ? reply.Body : _chatInput.TrimEnd() + " " + reply.Body;
        _repliesOpen = false;
    }

    // Yönetim sekmesi (yalnız yönetici)
    private List<SavedReplyItem> _savedReplies = [];
    private string? _replyEditId;
    private string  _replyTitle = string.Empty, _replyBody = string.Empty, _replyShortcut = string.Empty;
    private string? _replyFormError;

    private void EditReply(SavedReplyItem r)
    {
        _replyEditId = r.Id; _replyTitle = r.Title; _replyBody = r.Body; _replyShortcut = r.Shortcut ?? "";
        _replyFormError = null;
    }

    private void ResetReplyForm()
    {
        _replyEditId = null; _replyTitle = _replyBody = _replyShortcut = string.Empty; _replyFormError = null;
    }

    private async Task SaveReplyAsync()
    {
        var (saved, error) = await AdminApi.SaveSavedReplyAsync(_replyEditId, _replyTitle, _replyBody, _replyShortcut);
        if (saved is null)
        {
            _replyFormError = error;
            return;
        }
        Toast.ShowSuccess(_replyEditId is null ? "Hazır yanıt eklendi." : "Hazır yanıt güncellendi.");
        ResetReplyForm();
        _savedReplies = await AdminApi.GetSavedRepliesAsync();
    }

    private async Task DeleteReplyAsync(SavedReplyItem r)
    {
        if (!await JS.InvokeAsync<bool>("confirm", $"'{r.Title}' silinsin mi?")) return;
        if (await AdminApi.DeleteSavedReplyAsync(r.Id)) Toast.ShowSuccess("Hazır yanıt silindi.");
        if (_replyEditId == r.Id) ResetReplyForm();
        _savedReplies = await AdminApi.GetSavedRepliesAsync();
    }

    private void UseAssistDraft()
    {
        if (!string.IsNullOrWhiteSpace(_assist?.SuggestedReply))
            _chatInput = _assist.SuggestedReply!;
    }

    private void ResetAssist()
    {
        _assistOpen      = false;
        _assistLoading   = false;
        _assist          = null;
        _assistLoadError = null;
    }

    private static string AssistItemLabel(AgentAssistOpenItem item) =>
        item.Kind == "approval" ? $"Onay bekliyor: {item.Description}" : $"Eskalasyon: {item.Description}";

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
        if (!_isAgent) _agentPresence = await AdminApi.GetAgentPresenceAsync();
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

    /// <summary>
    /// Onaylı ama vektör hafızaya yazılamamış bir dersin yazımını yeniden dener.
    /// Aynı approve ucu çağrılır — <c>LessonMiner.ApproveAsync</c> bu durumu (Approved +
    /// VectorMemoryId boş) bir yeniden deneme olarak tanır ve mevcut karar gerekçesini korur.
    /// </summary>
    private async Task RetryLessonMemoryAsync(string lessonId)
    {
        await AdminApi.ApproveLessonAsync(lessonId, reason: null);
        _approvedLessons = await AdminApi.GetLessonsAsync("Approved");

        var still = _approvedLessons.FirstOrDefault(l => l.Id == lessonId);
        if (still is not null && string.IsNullOrEmpty(still.VectorMemoryId))
            Toast.ShowError("Hafızaya yazılamadı. Semantik hafıza kapalı ya da vektör veritabanına erişilemiyor olabilir.");
        else
            Toast.ShowSuccess("Ders hafızaya yazıldı; artık konuşmalarda kullanılacak.");
    }

    private async Task MineImprovementsAsync()
    {
        _miningInProgress = true;
        _miningStats      = null;
        var (candidates, proposed, error) = await AdminApi.MineImprovementsAsync();
        // Hata varsa sayaçlar anlamsız (-1) — "0 aday bulundu" gibi yanlış bir sonuç gösterme.
        _miningStats = error is not null
            ? $"Tarama çalıştırılamadı — {error}"
            : candidates == 0
                ? "Son tarama: incelenecek yeni aday trace bulunamadı."
                : $"Son tarama: {candidates} aday → {proposed} yeni öneri.";
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
    /// <summary>
    /// Otomatik yenileme aralığı.
    ///
    /// <para>
    /// Eskiden 3 saniyeydi ve panel kendi kendini rate-limit'liyordu: her tick'te
    /// RefreshBadgesAsync (3 istek) + RefreshActiveTabAsync (1-2 istek) çalışıyor, yani
    /// dakikada ~100 istek üretiliyordu. Sunucudaki "general" politikası ise IP başına
    /// <b>60 istek/dakika</b>. Sonuç: panel açık durduğu sürece kota tükeniyor ve
    /// "Yeni Tarama Çalıştır" gibi butonlar HTTP 429 alıyordu.
    /// </para>
    ///
    /// <para>
    /// 15 saniyede 4 tick/dakika × ~4 istek = ~16 istek/dakika — limitin çok altında,
    /// manuel işlemlere bol pay bırakıyor. Admin paneli için 15 sn hâlâ "canlı" hissettirir.
    /// </para>
    ///
    /// <para>
    /// IP başına ortak kota yine de yetmiyordu: aynı IP'deki yönetici, temsilci ve müşteri ekranları (ve SLA
    /// sayfası, açık sohbetin duygu yoklaması) 60'ı birlikte aşıyordu. Sunucu artık personeli kullanıcı başına
    /// ve daha yüksek kotayla bölümlüyor (<c>RateLimiting:StaffPerMinute</c>).
    /// </para>
    /// </summary>
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(15);

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
        }, null, AutoRefreshInterval, AutoRefreshInterval);
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
        // Ayrıca "çevrimdışı" gönderilmez: başka sekmede panel açık olabilir. Kalp atışı kesilince
        // sunucu zaman aşımıyla çevrimdışı sayar.
        if (_presenceTimer is not null) await _presenceTimer.DisposeAsync();
    }

    // ── Siparişler ────────────────────────────────────────────────────────────
    private async Task LookupOrderAsync()
    {
        if (string.IsNullOrWhiteSpace(_orderLookupId) || _orderBusy) return;
        _orderBusy  = true;
        _orderError = null;
        try
        {
            _orderView = await AdminApi.GetOrderAsync(_orderLookupId);
            if (_orderView is null) _orderError = $"#{_orderLookupId.Trim()} numaralı sipariş bulunamadı.";
            _orderCarrier = _orderView?.Carrier ?? string.Empty;
            _orderTracking = _orderView?.TrackingNumber ?? string.Empty;
        }
        finally { _orderBusy = false; }
    }

    private Task ShipOrderAsync() => UpdateOrderAsync(id => AdminApi.ShipOrderAsync(id,
        string.IsNullOrWhiteSpace(_orderCarrier) ? null : _orderCarrier, string.IsNullOrWhiteSpace(_orderTracking) ? null : _orderTracking));

    private Task DeliverOrderAsync() => UpdateOrderAsync(AdminApi.DeliverOrderAsync);

    private async Task UpdateOrderAsync(Func<string, Task<(OrderViewItem? Order, bool Changed, string? Error)>> update)
    {
        if (_orderView is null || _orderBusy) return;
        _orderBusy  = true;
        _orderError = null;
        try
        {
            var (order, changed, error) = await update(_orderView.OrderId);
            if (error is not null)
            {
                _orderError = error;
                return;
            }
            _orderView = order ?? _orderView;
            if (changed) Toast.ShowSuccess($"Sipariş #{_orderView.OrderId}: {_orderView.Status}. Müşteri e-postayla bilgilendirilir (e-posta açıksa).");
            else Toast.ShowInfo($"Sipariş zaten '{_orderView.Status}' durumunda — bildirim tekrar gönderilmedi.");
        }
        finally { _orderBusy = false; }
    }

    // ── Konuşma arama ─────────────────────────────────────────────────────────
    /// <summary>
    /// Gün sınırları tarayıcının yerel saatine göre: "5 Ekim" seçimi yerel 5 Ekim 00:00 – 6 Ekim 00:00
    /// aralığıdır; sunucuya UTC anları gider (sunucu saat dilimi varsaymaz).
    /// </summary>
    private static DateTime? LocalDayStartUtc(DateOnly? day) =>
        day is { } d ? DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local).ToUniversalTime() : null;

    private async Task SearchConversationsAsync(bool append)
    {
        if (_searching) return;
        _searching   = true;
        _searchError = null;
        try
        {
            var page = append ? _searchPage + 1 : 1;
            var (result, error) = await AdminApi.SearchConversationsAsync(
                _searchText, _searchCustomer, LocalDayStartUtc(_searchFrom), LocalDayStartUtc(_searchTo?.AddDays(1)),
                _searchReason, _searchTag, page);
            _searchedOnce = true;
            if (result is null)
            {
                _searchError = error;
                return;
            }
            _searchPage    = page;
            _searchHasMore = result.HasMore;
            _searchResults = append ? [.. _searchResults, .. result.Items] : [.. result.Items];
        }
        finally { _searching = false; }
    }

    private async Task ClearSearchAsync()
    {
        _searchText = _searchCustomer = _searchReason = _searchTag = string.Empty;
        _searchFrom = _searchTo = null;
        await SearchConversationsAsync(append: false);
    }

    private string ReasonLabel(string code) =>
        _searchReasons.FirstOrDefault(r => r.Code == code)?.Label ?? code;

    // ── Temsilci durumu ───────────────────────────────────────────────────────
    private async Task ConnectPresenceAsync()
    {
        _myPresence = await AdminApi.ConnectPresenceAsync();
        if (_myPresence is null) return;   // hesap bir temsilci kaydına bağlı değil — seçici gösterilmez
        _presenceTimer = new Timer(async _ =>
        {
            try
            {
                await InvokeAsync(async () =>
                {
                    if (await AdminApi.HeartbeatPresenceAsync() is { } fresh)
                    {
                        _myPresence = fresh;
                        StateHasChanged();
                    }
                });
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (Exception ex) { Console.Error.WriteLine($"[PresenceHeartbeat] {ex}"); }
        }, null, PresenceHeartbeatInterval, PresenceHeartbeatInterval);
    }

    private async Task OnMyPresenceChangedAsync(ChangeEventArgs e)
    {
        var chosen = e.Value?.ToString() ?? "online";
        if (await AdminApi.SetPresenceAsync(chosen) is { } updated)
        {
            _myPresence = updated;
            Toast.ShowSuccess($"Durumunuz: {PresenceLabel(updated.ChosenPresence)}.");
        }
        else
        {
            Toast.ShowError("Durum güncellenemedi.");
        }
    }

    private static string PresenceLabel(string? presence) => presence switch
    {
        "online" => "Çevrimiçi",
        "away"   => "Uzakta",
        _        => "Çevrimdışı"
    };

    private AgentPresenceItem? PresenceOf(string agentId) =>
        _agentPresence.FirstOrDefault(p => p.AgentId == agentId);

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string ShortId(string? id) =>
        id is null ? "—" : id[..Math.Min(8, id.Length)] + "…";

    /// <summary>
    /// Onayın kendisi değil, onayın SONUCU. Bu ayrım panelde görünür olmalı: yalnızca karara
    /// bakan bir liste, yürütmesi başarısız olmuş ya da yarıda kalmış bir talebi de sorunsuz
    /// "Approved" gösteriyordu — yani düzeltilmesi gereken kayıt hiçbir yerde göze çarpmıyordu.
    /// </summary>
    /// <summary>
    /// Bir yürütmenin gerçekten askıda mı yoksa hâlâ çalışıyor mu olduğuna sunucu karar verir
    /// (<c>ApprovalOptions.StuckExecutionAfterMinutes</c>); panel o kararı <c>/approvals/stuck</c>
    /// listesinden okur, kendi eşiğini tutmaz.
    ///
    /// <para>
    /// Bu ayrım olmadan geçmiş ekranındaki HER <c>running</c> kayıt "deploy/crash oldu, elle
    /// doğrulayın" uyarısı alıyordu — uzun süren ama sorunsuz devam eden bir işlem de dahil.
    /// Yanlış alarm, uyarının kendisini değersizleştirir.
    /// </para>
    /// </summary>
    private bool IsStuck(ApprovalRequest a) => _stuckApprovals.Any(s => s.Id == a.Id);

    private static string ExecutionStatusLabel(string? status, bool isStuck = false) => status switch
    {
        "running"   => isStuck ? "işlem askıda" : "işleniyor",
        "failed"    => "işlem başarısız",
        "succeeded" => "işlem tamam",
        _           => ""
    };

    // ── Onay kartı sunumu ─────────────────────────────────────────────────────
    //
    // Admin'in vereceği karar tek bir soruya dayanır: "kimin adına, ne yapılacak?"
    // Kart bu soruyu en üstte ve kod diliyle değil insan diliyle yanıtlar; makine
    // kimlikleri (tool adı, kayıt no, session, ham parametreler) "Teknik ayrıntı"
    // altına iner. 50 bekleyen isteği tarayan bir admin her kartı saniyeler içinde
    // okuyabilmelidir.
    //
    // NOT: Tool adları burada string sabit olarak duruyor çünkü Web projesi Domain'e
    // referans vermez (bilinçli — WASM bundle'ı sunucu tiplerini taşımasın diye).
    // Tanınmayan bir tool adı geldiğinde kart jenerik anahtar/değer listesine düşer,
    // yani senkron kayması veri kaybına değil yalnızca daha ham bir görünüme yol açar.

    private const string ToolOrderPlacement = "order_placement_tool";
    private const string ToolOrderCancel    = "order_cancel_tool";
    private const string ToolReturnRequest  = "return_request_tool";
    private const string ToolComplaint      = "complaint_registration_tool";

    /// <summary>Sipariş satırı — çok ürünlü siparişin tek kalemi.</summary>
    private sealed record ApprovalLine(string Product, int Quantity);

    /// <summary>Karta yazılacak tek bir olgu. <paramref name="Multiline"/> uzun serbest metinler için.</summary>
    private sealed record ApprovalFact(string Label, string Value, bool Multiline = false);

    /// <summary>Tool adının admin'e gösterilen insan-okunur karşılığı.</summary>
    private static string ToolDisplayName(string toolName) => toolName switch
    {
        ToolOrderPlacement => "Yeni Sipariş",
        ToolOrderCancel    => "Sipariş İptali",
        ToolReturnRequest  => "İade Talebi",
        ToolComplaint      => "Şikayet Kaydı",
        _                  => toolName
    };

    /// <summary>Onay/ret bildiriminin öznesi: kartın başlığıyla aynı ad; tanınmayan işlemde ham araç adı yerine "Talep".</summary>
    private string ApprovalSubject(string approvalId)
    {
        var tool = _pendingApprovals.FirstOrDefault(a => a.Id == approvalId)?.ToolName;
        return tool is ToolOrderPlacement or ToolOrderCancel or ToolReturnRequest or ToolComplaint
            ? ToolDisplayName(tool)
            : "Talep";
    }

    /// <summary>Onay kararının admin'e gösterilen Türkçe karşılığı (sunucu durumu: approved/rejected/expired/pending).</summary>
    private static string DecisionLabel(string? status) => status switch
    {
        "approved" => "Onaylandı",
        "rejected" => "Reddedildi",
        "expired"  => "Zaman aşımı",
        "pending"  => "Bekliyor",
        _          => status ?? "—"
    };

    /// <summary>Sayfa başlığı — kenar çubuğundaki bölüm adıyla aynı.</summary>
    private string SectionTitle => _activeTab switch
    {
        "approvals"     => "Onaylar",
        "escalations"   => "Eskalasyonlar",
        "chats"         => "Canlı sohbetler",
        "conversations" => "Konuşmalar",
        "orders"        => "Siparişler",
        "history"       => "Karar geçmişi",
        "analytics"     => "Analiz",
        "replies"       => "Hazır yanıtlar",
        "improvements"  => "İyileştirme önerileri",
        _               => "Destek Konsolu"
    };

    private string SectionDescription => _activeTab switch
    {
        "approvals"     => "Müşterilerin sipariş, iptal, iade ve şikayet talepleri. En uzun bekleyen en üstte; çok uzun süre (günler) yanıtsız kalan talebi sistem otomatik reddeder.",
        "escalations"   => _isAgent ? "Size atanan ve atanmayı bekleyen konuşmalar." : "Asistanın çözemediği ve bir temsilcinin bakması gereken konuşmalar.",
        "chats"         => "Temsilcinin devraldığı canlı konuşmalar. Bir eskalasyonu devraldığınızda burada açılır.",
        "conversations" => "Kapanmış ve süren konuşmalarda mesaj, müşteri, tarih ve etikete göre arama.",
        "orders"        => "Sipariş durumunu güncelleyin; durum değişince müşteriye e-posta gider.",
        "history"       => "Son 50 onay kararı ve son 50 eskalasyon.",
        "analytics"     => "Genel istatistikler; bir oturum seçerek ayrıntısını görün.",
        "replies"       => "Temsilcilerin canlı sohbette tek tıkla ekleyebildiği yanıtlar.",
        "improvements"  => "Düşük puanlı ya da hatalı konuşmalardan çıkarılan ders önerileri. Onaylanan ders asistanın hafızasına yazılır.",
        _               => ""
    };

    /// <summary>Eskalasyon durumunun Türkçe karşılığı (sunucu: open/acknowledged/resolved/dismissed).</summary>
    private static string EscalationStatusLabel(string? status) => status switch
    {
        "open"         => "Atanmamış",
        "acknowledged" => "Üstlenildi",
        "resolved"     => "Çözüldü",
        "dismissed"    => "Kapatıldı",
        _              => status ?? "—"
    };

    /// <summary>Duygu etiketinin Türkçe karşılığı.</summary>
    private static string SentimentLabel(string? label) => label?.ToLowerInvariant() switch
    {
        "positive" => "Olumlu",
        "negative" => "Olumsuz",
        "angry"    => "Öfkeli",
        "neutral"  => "Nötr",
        null       => "Nötr",
        _          => label
    };

    /// <summary>Asistan (ajan) adının panelde gösterilen karşılığı; tanınmayan ad olduğu gibi kalır.</summary>
    private static string AgentDisplayName(string? agentName) => agentName switch
    {
        null or ""                          => "—",
        "OrderAgent" or "OrderInfoAgent"    => "Sipariş asistanı",
        "ComplaintAgent" or "ComplaintInfoAgent" => "Şikayet asistanı",
        "ProductAgent" or "ProductInfoAgent" => "Ürün asistanı",
        "PlanningAgent"                     => "Planlama",
        "ResponseAgent"                     => "Yanıt asistanı",
        "HumanHandoffAgent"                 => "Temsilciye aktarım",
        _                                   => agentName
    };

    private static string LessonStatusLabel(string? status) => status switch
    {
        "Approved" => "Onaylandı",
        "Rejected" => "Reddedildi",
        "Proposed" => "Öneri",
        _          => status ?? "—"
    };

    /// <summary>Talep türünün ikonu (Icon bileşeni adı) — türü bir bakışta ayırt ettirir.</summary>
    private static string ToolIcon(string toolName) => toolName switch
    {
        ToolOrderPlacement => "cart",
        ToolOrderCancel    => "x-circle",
        ToolReturnRequest  => "undo",
        ToolComplaint      => "complaint",
        _                  => "tool"
    };

    /// <summary>İkon kutusunun renk tonu (CSS sınıfı) — tür başına sabit.</summary>
    private static string ToolTone(string toolName) => toolName switch
    {
        ToolOrderPlacement => "tone-success",
        ToolOrderCancel    => "tone-danger",
        ToolReturnRequest  => "tone-primary",
        ToolComplaint      => "tone-warn",
        _                  => "tone-neutral"
    };

    /// <summary>
    /// Sipariş satırlarını okur. Yalnızca <c>order_placement_tool</c> bu alanı taşır;
    /// diğer tool'larda null döner ve kart olgu listesine düşer.
    /// </summary>
    private static List<ApprovalLine>? ApprovalLines(object? parameters)
    {
        if (ParamElement(parameters, "lines") is not { ValueKind: JsonValueKind.Array } array)
            return null;

        var lines = new List<ApprovalLine>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var product = ReadProp(element, "productName");
            var qty = ReadProp(element, "quantity");
            if (product is not { ValueKind: JsonValueKind.String }) continue;

            lines.Add(new ApprovalLine(
                product.Value.GetString() ?? "",
                qty?.ValueKind switch
                {
                    JsonValueKind.Number when qty.Value.TryGetInt32(out var n) => n,
                    JsonValueKind.String when int.TryParse(qty.Value.GetString(), out var n) => n,
                    _ => 0
                }));
        }

        return lines.Count > 0 ? lines : null;
    }

    /// <summary>
    /// Tanınan tool'lar için kararın dayanağı olan olguları insan diliyle döner.
    /// Tanınmayan tool'da <c>null</c> — kart o zaman ham anahtar/değer listesini çizer.
    /// </summary>
    private static List<ApprovalFact>? ApprovalFacts(ApprovalRequest a)
    {
        string? P(string key) => ParamString(a.Parameters, key);

        return a.ToolName switch
        {
            ToolOrderCancel =>
            [
                new ApprovalFact("Sipariş", FormatOrderId(P("orderId"))),
                new ApprovalFact("İptal sebebi", P("reason") ?? "—", Multiline: true)
            ],
            ToolReturnRequest =>
            [
                new ApprovalFact("Sipariş", FormatOrderId(P("orderId"))),
                new ApprovalFact("İade sebebi", P("reason") ?? "—", Multiline: true)
            ],
            ToolComplaint =>
            [
                new ApprovalFact("Sipariş", FormatOrderId(P("orderId"))),
                new ApprovalFact("Şikayet", P("complaintText") ?? "—", Multiline: true)
            ],
            _ => null
        };
    }

    private static string FormatOrderId(string? orderId) =>
        string.IsNullOrWhiteSpace(orderId) ? "—" : $"#{orderId}";

    /// <summary>
    /// Gerekçe, PlanningAgent bir rationale üretemediğinde jenerik bir şablona düşer
    /// (<c>"{agent} bu tool'u çağırmak istiyor."</c>). O metin kartta yer kaplar ama
    /// hiçbir şey söylemez — karar için bilgi taşımayan satır gösterilmez.
    /// </summary>
    private static bool HasMeaningfulJustification(string? justification) =>
        !string.IsNullOrWhiteSpace(justification)
        && !justification.Contains("bu tool'u çağırmak istiyor", StringComparison.OrdinalIgnoreCase);

    private static JsonElement? ParamElement(object? parameters, string key)
    {
        if (parameters is not JsonElement root || root.ValueKind != JsonValueKind.Object)
            return null;

        return ReadProp(root, key);
    }

    private static string? ParamString(object? parameters, string key) =>
        ParamElement(parameters, key) switch
        {
            { ValueKind: JsonValueKind.String } e => e.GetString(),
            { ValueKind: JsonValueKind.Null } => null,
            { } e => e.GetRawText(),
            _ => null
        };

    /// <summary>Büyük/küçük harfe duyarsız özellik okuma — sunucu camelCase yazar, JSON round-trip biçimi değişebilir.</summary>
    private static JsonElement? ReadProp(JsonElement element, string name)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }
        return null;
    }

    /// <summary>
    /// Onay kaydına bağlanmış müşteri fotoğraflarının kimlikleri (<c>Parameters["attachmentIds"]</c>,
    /// bkz. SideEffectApprovalGate). Yoksa boş liste.
    /// </summary>
    private static List<string> AttachmentIds(object? parameters)
    {
        if (parameters is not JsonElement root || root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("attachmentIds", out var ids) || ids.ValueKind != JsonValueKind.Array)
            return [];
        return ids.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

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
                JsonValueKind.Array  => FormatArray(prop.Value),
                _                    => prop.Value.GetRawText()
            };
            list.Add((prop.Name, val));
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// Dizi parametreleri okunur tek satıra indirir — çok ürünlü siparişin
    /// <c>lines</c> alanı ham JSON olarak gösterilirse admin neyi onayladığını
    /// göremez ve HITL kapısı anlamını yitirir.
    ///
    /// <para>
    /// Nesne elemanlar <c>"alan=değer · alan=değer"</c> biçiminde düzleştirilir; şema
    /// bilinmediği için alan adları olduğu gibi yazılır (bu yardımcı yalnızca sipariş
    /// satırlarına özel değil, herhangi bir dizi parametreye uygulanır).
    /// </para>
    /// </summary>
    private static string FormatArray(JsonElement array)
    {
        var items = new List<string>();
        foreach (var element in array.EnumerateArray())
        {
            items.Add(element.ValueKind switch
            {
                JsonValueKind.Object => string.Join(" · ", element.EnumerateObject().Select(p =>
                    $"{p.Name}={(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())}")),
                JsonValueKind.String => element.GetString() ?? "",
                _ => element.GetRawText()
            });
        }
        return items.Count == 0 ? "—" : string.Join("  |  ", items);
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
