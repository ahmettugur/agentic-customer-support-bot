// Models/ApprovalRequest.cs
// HITL — Human-in-the-Loop approval gate modelleri.
// Yüksek riskli tool çağrıları (OrderPlacement, ComplaintRegistration) öncesi
// Kullanıcı/admin onayı bekler. TaskCompletionSource ile async wait tarafı
// IApprovalQueue implementasyonunda tutulur; bu model sadece veri taşır.

namespace CustomerSupportBot.Domain.Model;

/// <summary>Approval isteğinin yaşam döngüsü.</summary>
public enum ApprovalStatus
{
    Pending,     // Onay bekliyor
    Approved,    // Admin onayladı
    Rejected,    // Admin reddetti
    Expired      // Timeout ile otomatik reddedildi
}

/// <summary>
/// Onaylanan bir talebin GERÇEK İŞİNİN (sipariş iptali, iade...) durumu.
///
/// <para>
/// <see cref="ApprovalStatus"/>'ten AYRI tutulur çünkü ikisi farklı sorular yanıtlar:
/// biri "insan ne karar verdi", diğeri "o karar hayata geçti mi". Tek alanda birleştirilince
/// yürütmesi başarısız olmuş bir talep panelde sadece "Onaylandı" görünüyordu; daha kötüsü,
/// karar yazıldıktan sonra yürütme çalışmadan süreç kapanırsa (deploy, crash) kayıt hiçbir
/// yerde "askıda" görünmüyor, sessizce kayboluyordu.
/// </para>
/// </summary>
public enum ApprovalExecutionStatus
{
    /// <summary>Yürütülecek bir iş yok — talep reddedildi veya henüz karara bağlanmadı.</summary>
    None,

    /// <summary>Onaylandı, gerçek iş başlatıldı ama sonucu henüz yazılmadı.</summary>
    Running,

    /// <summary>Gerçek iş başarıyla tamamlandı.</summary>
    Succeeded,

    /// <summary>Gerçek iş çalıştı ve başarısız oldu (hata veya tool'un kendi reddi).</summary>
    Failed
}

/// <summary>
/// Bir tool çağrısı için oluşturulmuş approval kaydı. Tool lambda'sı bu
/// Nesneyi queue'ya yazar ve decision'ını await eder.
/// </summary>
public class ApprovalRequest
{
    /// <summary>Benzersiz kimlik (admin endpoint'lerinde kullanılır).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Hangi workflow/session bu talebi üretti.</summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Login'li müşterinin doğrulanmış kimliği (bkz. SessionState.AuthenticatedCustomerId).
    /// SessionId'ye ek bir bağ — müşteri farklı bir sekmede/session'da tekrar login olsa bile
    /// gelecekte müşteri bazlı sorgulama (ör. "tüm hesabına ait bildirimler") için temel oluşturur.
    /// </summary>
    public string? CustomerId { get; set; }

    /// <summary>
    /// Müşterinin adı — <b>kalıcı değildir</b>, admin paneline gönderilmeden hemen önce
    /// <c>ApprovalPortService</c> tarafından doldurulur (bkz. <c>ICustomerRepository.GetFullNamesAsync</c>).
    ///
    /// <para>
    /// Neden okuma anında: ad, onayın bir parçası değil bir <i>görüntüleme</i> alanıdır. Kayıt
    /// anında yazılıp saklansaydı müşteri adını değiştirdiğinde panelde eski ad görünürdü ve
    /// bunu düzeltmek için bir migration gerekirdi. <see cref="ReasonRequired"/> ile aynı ruh:
    /// panelin ihtiyacı olan türetilmiş bilgi sunucuda hesaplanır, panel kendi kopyasını tutmaz.
    /// </para>
    ///
    /// <para>
    /// Müşteri silinmiş veya kimlik çözülemiyorsa <c>null</c> kalır; panel o zaman yalnızca
    /// numarayı gösterir.
    /// </para>
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>Trace ID — admin panelinden bağlam görmek için.</summary>
    public string? TraceId { get; set; }

    /// <summary>Örn. "order_placement_tool".</summary>
    public string ToolName { get; set; } = "";

    /// <summary>Hangi agent bu tool'u çağırıyor (insan okunur).</summary>
    public string? AgentName { get; set; }

    /// <summary>Parametreler — admin'in kararı verirken göreceği veri.</summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();

    /// <summary>Kullanıcı sorusu (bağlam için).</summary>
    public string? UserQuery { get; set; }

    /// <summary>
    /// Admin'e gösterilen "bu tool neden çağrılıyor" gerekçesi — pratikte PlanningAgent'ın
    /// routing rationale'ı (bkz. <c>WorkflowRunner.ResolveApprovalJustification</c>).
    ///
    /// <para>
    /// NOT: Bu alan eskiden "preToolCheck.reasoning" olarak belgelenmişti ama öyle
    /// doldurulamaz — <c>preToolCheck</c>, uzman ajanın FINAL yapılandırılmış JSON'ının
    /// parçasıdır (yanında <c>postToolReflection</c> ile birlikte, yani tool çalıştıktan
    /// sonra üretilir); onay ise tool çalışmadan önce tetiklenir. O anda mevcut olan en
    /// bilgilendirici gerekçe planlama turunun rationale'ıdır.
    /// </para>
    /// </summary>
    public string? Justification { get; set; }

    /// <summary>İsteğin oluşturulma zamanı.</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Karar verilme zamanı (null → Pending).</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Mevcut durum.</summary>
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>Kararı veren admin kullanıcı adı (opsiyonel).</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Red/expire gerekçesi (kullanıcıya gösterilir).</summary>
    public string? DecisionReason { get; set; }

    /// <summary>Bu request için timeout (saniye). Config'ten gelir.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Onaylandıktan sonra gerçek işin (sipariş iptali vb.) yürütme sonucu —
    /// <see cref="IApprovalExecutionRouter"/> tarafından doldurulur. Reddedilen/expire olan
    /// isteklerde null kalır (yürütülmedi).
    /// </summary>
    public string? ExecutionResult { get; set; }

    /// <summary>Yürütmenin tamamlandığı zaman (null → henüz yürütülmedi veya onaylanmadı).</summary>
    public DateTime? ExecutedAt { get; set; }

    /// <summary>
    /// Gerçek işin durumu — insan kararından (<see cref="Status"/>) bağımsızdır.
    /// <see cref="ApprovalExecutionStatus.Running"/> kalmış bir kayıt, yürütme sırasında
    /// sürecin kapandığı anlamına gelir ve insan müdahalesi gerektirir.
    /// </summary>
    public ApprovalExecutionStatus ExecutionStatus { get; set; } = ApprovalExecutionStatus.None;

    /// <summary>
    /// Müşterinin bu kararı/sonucu (bildirim/badge olarak) gördüğü zaman. Null ise "unseen" —
    /// kullanıcı chat'e dönüp geçmiş bildirimlerini çektiğinde bu alan doldurulur.
    /// </summary>
    public DateTime? CustomerSeenAt { get; set; }

    /// <summary>
    /// Bu tool'un onaylanması için gerekçe (audit trail) zorunlu mu — yani
    /// <see cref="WellKnown.HighRiskTools"/> içinde mi. Salt-okunur ve türetilmiş;
    /// JSON'a serileştirilerek admin paneline gider.
    ///
    /// <para>
    /// Amaç: panelin aynı listeyi kendi tarafında tekrar tutmasını önlemek. Panel eskiden
    /// kendi <c>HighRiskTools</c> kopyasını tutuyordu ve bu kopya sunucudaki listeyle
    /// senkronu kaybetmişti — order_cancel_tool/return_request_tool için "gerekçe isteğe
    /// bağlı" gösterilip onay backend'de 400 (<c>approval_reason_required</c>) ile
    /// reddediliyordu. Karar tek yerde (sunucuda) verilir, panel sadece uygular.
    /// </para>
    /// </summary>
    public bool ReasonRequired => WellKnown.HighRiskTools.Contains(ToolName);
}

