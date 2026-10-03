// Application/Services/Approval/SideEffectApprovalGate.cs
// Yan etkili tool çağrılarının HITL onay kapısı — yazılı ve sesli kanalın ORTAK tek kaynağı.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Approval;

/// <summary>
/// Yan etkili bir tool çağrısını (sipariş/iptal/iade/şikayet) onaya gönderir ya da — onay
/// gerekmiyorsa — doğrudan çalıştırır.
///
/// <para>
/// <b>Onay BLOKLAMAZ:</b> onay gerekiyorsa kayıt oluşturulur ve KARARI BEKLEMEDEN hemen
/// <see cref="ToolResult.Pending"/> döner. Gerçek iş admin karar verdiğinde
/// <see cref="IApprovalExecutionRouter"/> üzerinden ayrıca yürütülür; sonuç kullanıcıya
/// bildirim olarak ulaşır.
/// </para>
///
/// <para>
/// <b>Neden ayrı sınıf:</b> bu mantık eskiden yalnızca yazılı sohbetin tool kurucusunda
/// (<c>Adapters.Agents.ApprovalGateService</c>) private bir metottu; sesli kanal ona
/// erişemediği için yan etkili işlemler sesle hiç yapılamıyordu. Kural tek yerde durur:
/// hangi kanaldan gelirse gelsin yan etkili her işlem aynı insan onayından geçer.
/// </para>
/// </summary>
public sealed class SideEffectApprovalGate
{
    /// <summary>
    /// DB'deki <c>param_signature</c> kolonunun sınırı (bkz. ApprovalRequestConfiguration) —
    /// bunu aşan bir imza dedup için kullanılamaz, benzersiz bir imzaya düşülür.
    /// </summary>
    private const int MaxParamSignatureLength = 1000;

    private readonly IApprovalQueue _approvalQueue;
    private readonly ApprovalOptions _options;
    private readonly ILogger _logger;

    public SideEffectApprovalGate(
        IApprovalQueue approvalQueue,
        IOptions<ApprovalOptions> options,
        ILogger? logger = null)
    {
        _approvalQueue = approvalQueue;
        _options = options.Value;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Bu tool için insan onayı gerekiyor mu (yapılandırmaya göre)?</summary>
    public bool RequiresApproval(string toolName) =>
        _options.Enabled && _options.ToolsRequiringApproval.Contains(toolName);

    /// <summary>
    /// Onay gerekmiyorsa <paramref name="executeDirectly"/>'yi çalıştırır. Gerekiyorsa önce
    /// <paramref name="preflight"/>'ı çalıştırır, sonra onay kaydını oluşturur ve "onaya
    /// gönderildi" sonucunu döner.
    ///
    /// <para>
    /// <paramref name="preflight"/> — onay kaydı OLUŞTURULMADAN önce çalışan salt-okunur ön
    /// kontrol (ör. sipariş var mı, bu müşteriye ait mi). Gerçek iş admin kararından sonra
    /// çalıştığı için bu kontrol olmasaydı, baştan başarısız olacağı belli bir talep önce kuyruğa
    /// düşer, admin'in zamanını harcar, onaylanır ve ancak o zaman başarısız olurdu. Yürütme
    /// anındaki kontrolün YERİNE geçmez (durum arada değişebilir) — birlikte çalışırlar.
    /// </para>
    ///
    /// <para>
    /// Mükerrer talep engellemesi "önce oku sonra yaz" ile YAPILMAZ (TOCTOU ve çok-pod yarışı):
    /// her zaman <see cref="IApprovalQueue.CreateAsync"/> çağrılır, dedup DB'deki kısmi unique
    /// index'te atomik olarak yapılır — aynı imzalı bekleyen kayıt varsa o geri döner.
    /// </para>
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, object?> parameters,
        Func<ToolResult> executeDirectly,
        ApprovalContext? context,
        Func<ToolResult?>? preflight = null)
    {
        if (!RequiresApproval(toolName))
            return executeDirectly();

        if (preflight?.Invoke() is { } blocked)
        {
            _logger.LogInformation(
                "[HITL] Onay kaydı oluşturulmadı — ön kontrol reddetti tool={Tool} code={Code}",
                toolName, blocked.Error?.Code);
            return blocked;
        }

        var agentName = ResolveAgentName(toolName);
        var request = new ApprovalRequest
        {
            SessionId = context?.SessionId,
            CustomerId = context?.CustomerId,
            TraceId = context?.TraceId,
            UserQuery = context?.UserQuery,
            ToolName = toolName,
            AgentName = agentName,
            Parameters = parameters,
            ParamSignature = BuildParamSignature(parameters),
            Justification = string.Format(WellKnown.ApprovalReasons.AgentWantsToCall, agentName),
            TimeoutSeconds = _options.TimeoutSeconds
        };
        request = await _approvalQueue.CreateAsync(request);

        return ToolResult.Pending(string.Format(WellKnown.FallbackMessages.ApprovalPending, request.Id));
    }

    /// <summary>
    /// Hangi ajanın hangi tool'u sahiplendiği — admin panelinde "hangi ajan istiyor" bilgisi.
    /// Eşleme <see cref="WellKnown.SideEffectToolOwners"/>'dan okunur.
    /// </summary>
    public static string ResolveAgentName(string toolName) =>
        WellKnown.SideEffectToolOwners.TryGetValue(toolName, out var agentName)
            ? agentName
            : "UnknownAgent";

    /// <summary>
    /// Mükerrer onay talebi tespiti için parametrelerin kanonik imzası.
    ///
    /// <para>
    /// Eskiden imza <c>"anahtar=deger"</c> parçalarının <c>|</c> ile birleştirilmesiydi ve iki
    /// ayrı şekilde yanlış cevap veriyordu:
    /// </para>
    ///
    /// <para>
    /// <b>1. Yanlış EŞLEŞME.</b> Ayraçlar kaçışlanmadığı için <c>{a:"x|b=y"}</c> ile
    /// <c>{a:"x", b:"y"}</c> aynı imzayı üretiyordu. Farklı iki talep aynı sayıldığında
    /// ikincisi sessizce birincinin kaydına katlanır ve hiçbir zaman yürütülmez.
    /// </para>
    ///
    /// <para>
    /// <b>2. Yanlış AYRIŞMA.</b> <c>ToString()</c> geçerli kültürü kullanır. Kayıt Redis veya
    /// veritabanından geri okunduğunda değerler <c>JsonElement</c> olur ve onların
    /// <c>ToString()</c>'i ham JSON metnidir — kültürden bağımsız. Türkçe kurulumda ölçülen
    /// fark: taze <c>amount=100,5</c> ile round-trip <c>amount=100.5</c>. Yani BAŞKA bir pod'da
    /// oluşmuş (ya da restart'tan sonra DB'den yüklenmiş) bekleyen bir talep, aynı parametrelerle
    /// gelen yeni çağrıyla asla eşleşmez. Mükerrer koruma tam da en çok gerektiği yerde —
    /// çok pod'lu kurulumda — sessizce devre dışı kalır: iki onay kaydı oluşur, admin ikisini
    /// de onaylarsa iş İKİ KEZ yürütülür.
    /// </para>
    ///
    /// <para>
    /// JSON serileştirme her ikisini birden çözer: yapısal olarak kaçışlıdır (çakışma olmaz) ve
    /// sayı biçimi kültürden bağımsızdır, dolayısıyla CLR değeri ile onun <c>JsonElement</c>
    /// karşılığı aynı metni üretir.
    /// </para>
    /// </summary>
    public static string BuildParamSignature(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0) return string.Empty;

        // Anahtar sırası çağrıdan çağrıya değişebilir; imza sıradan bağımsız olmalı.
        var ordered = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in parameters) ordered[kv.Key] = kv.Value;

        string signature;
        try { signature = JsonSerializer.Serialize(ordered); }
        catch (NotSupportedException)
        {
            // Serileştirilemeyen bir değer imza üretimini engellememeli: mükerrer koruması bir
            // OPTİMİZASYONDUR, onay akışının kendisi değil. Benzersiz bir imza dönülür — talep
            // "mükerrer" sayılıp yanlışlıkla bastırılmaz, yalnızca kendi kaydını alır.
            return Guid.NewGuid().ToString("N");
        }

        // Alışılmadık büyüklükte bir parametre kümesi DB kolon sınırını aşarsa dedup'tan çıkarılır;
        // yoksa iki FARKLI büyük talep aynı (kesilmiş) imzayla yanlışlıkla birleşebilirdi.
        return signature.Length <= MaxParamSignatureLength
            ? signature
            : Guid.NewGuid().ToString("N");
    }
}
