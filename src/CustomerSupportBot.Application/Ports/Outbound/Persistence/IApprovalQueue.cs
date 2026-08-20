using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// HITL onay kuyruğu için secondary port.
///</summary>
public interface IApprovalQueue
{
    Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken ct = default);
    Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct = default);
    Task<bool> DecideAsync(string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default);
    /// <summary>
    /// Bekleyen kayıtların <b>süreç-içi cache görünümü</b>. Hızlıdır ama EKSİK olabilir:
    /// başka bir pod'un oluşturduğu kayıt bu pod'a Redis pub/sub ile ulaşır ve o mesaj
    /// kaybolabilir (pub/sub en fazla bir kez teslim eder; Redis restart'ı veya ağ kesintisi
    /// mesajı düşürür). Cache bir kez hydrate olduktan sonra bir daha DB'ye bakmadığı için
    /// böyle bir kayıp KALICI olur.
    ///
    /// <para>
    /// Bu yüzden yalnızca eksikliğin zararsız olduğu yerde kullanılmalıdır — pratikte tek
    /// meşru kullanımı, kaydı bu pod'un kendisinin oluşturduğu mükerrer-çağrı kontrolüdür.
    /// "Bekleyen işler" listesi, SLA ve süpürme gibi <b>eksikliğin sessiz bir kayba
    /// dönüştüğü</b> her yerde <see cref="GetPendingAsync"/> kullanılmalıdır.
    /// </para>
    /// </summary>
    IReadOnlyList<ApprovalRequest> GetPending();

    /// <summary>
    /// Bekleyen kayıtların <b>kalıcı</b> listesi — kayıtların gerçek kaynağından okur.
    ///
    /// <para>
    /// Bir onay kaydının görünmemesi sessiz bir başarısızlıktır: müşteri talebini göndermiştir,
    /// admin panelinde talep hiç belirmez, süpürme onu bulamadığı için zaman aşımına da
    /// uğratılmaz. Talep sonsuza kadar bekler ve kimse bunu fark etmez. Bu okuma bu yüzden
    /// cache'e güvenmez.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default);
    IReadOnlyList<ApprovalRequest> GetRecent(int count = 50);
    ApprovalRequest? Get(string id);

    /// <summary>
    /// Bu session'a ait, karara bağlanmış (Approved/Rejected/Expired) ama müşterinin henüz
    /// bildirim olarak görmediği (CustomerSeenAt == null) kayıtlar. Kullanıcı chat'e geri
    /// döndüğünde (yeni sekme/sayfa yenileme) kaçırdığı onay sonuçlarını görebilsin diye.
    ///
    /// <para>
    /// <b>Kalıcı okumadır</b> — cache'e değil, kayıtların gerçek kaynağına gitmelidir. Anlık
    /// bildirim (SSE) Redis'e bağlı olabilir ve Redis mesajı kaybolabilir; "sonradan girince
    /// gör" akışı ise kaybolmamalıdır.
    /// </para>
    /// </summary>
    /// <para>
    /// <paramref name="customerId"/> ZORUNLU bir daraltmadır, kolaylık değil. Yalnızca sessionId
    /// ile sorgulamak yetmez: onay kayıtları oturumdan bağımsız yaşar (session_id için yabancı
    /// anahtar yoktur), dolayısıyla oturum silinse bile kayıt kalır ve sessionId'yi öğrenen
    /// başka bir müşteri bildirimi okuyabilirdi. Oturum sahiplik kontrolü de var olmayan
    /// oturumlara izin verir — ilk temasın oturumu çağırana bağlaması için.
    /// </para>
    Task<IReadOnlyList<ApprovalRequest>> GetUnseenForSessionAsync(
        string sessionId, string customerId, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcı bildirimi gördüğünde CustomerSeenAt'i işaretler. Yazma başarılıysa
    /// <c>true</c> döner.
    ///
    /// <para>
    /// Sonuç DÖNMESİ gerekir: hata yutulup çağırana başarı bildirilirse istemci bildirimi
    /// "okundu" sayar ama kayıt işaretlenmemiştir; aynı bildirim her girişte yeniden çıkar ve
    /// sebebi hiçbir yerde görünmez.
    /// </para>
    /// </summary>
    Task<bool> MarkSeenAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Tek bir kaydı <b>kalıcı depodan</b> okur. <see cref="Get"/> cache'e bakar ve cache
    /// yalnızca açık kayıtlar + son N kararı tutar; bu yüzden cache, kalıcı sorguların
    /// (ör. <see cref="GetUnseenForSessionAsync"/>) döndürebildiği eski bir kaydı bilmeyebilir.
    /// Sahiplik/yetki kontrolü yapan uçlar bu metodu kullanmalıdır — aksi hâlde listelenen bir
    /// kayıt için yapılan işlem 404 döner.
    /// </summary>
    Task<ApprovalRequest?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Bu müşteriye ait TÜM onay taleplerini (görülmüş/görülmemiş, karara bağlanmış/bekleyen
    /// fark etmeksizin) en yeniden eskiye sıralı döner — "geçmiş işlemlerim" görünümü için.
    /// SessionId'ye değil CustomerId'ye göre sorgular, bu yüzden müşteri farklı bir cihazda/
    /// sekmede tekrar login olsa bile aynı geçmişi görür.
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> GetHistoryForCustomerAsync(
        string customerId, int count = 100, CancellationToken ct = default);

    /// <summary>
    /// Onaylanmış ama yürütmesi <c>Running</c>'de <b>takılıp kalmış</b> kayıtlar — yürütme
    /// sırasında sürecin kapandığı (deploy/crash) durumlar.
    ///
    /// <para>
    /// <c>ApprovalOptions.StuckExecutionAfterMinutes</c> kadar eskimiş olanlar döner; hâlâ
    /// çalışıyor olabilecek taze kayıtlar dahil edilmez.
    /// </para>
    ///
    /// <para>
    /// <b>Kalıcı depodan ve tarih sınırı olmadan</b> sorgulanır. "Son N kayıt" listeleri bu iş
    /// için yetersizdir: askıda kalan bir kayıt trafik arttıkça listeden düşer ve tam da elle
    /// müdahale bekleyen kayıt görünmez olur.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct = default);

    event EventHandler<ApprovalRequest>? RequestCreated;
    event EventHandler<ApprovalRequest>? RequestDecided;
}
