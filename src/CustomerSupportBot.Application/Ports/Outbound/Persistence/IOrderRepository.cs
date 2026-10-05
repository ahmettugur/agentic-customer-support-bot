using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>Durum geçişinin sonucu.</summary>
public enum OrderStatusChange
{
    Updated,
    /// <summary>Sipariş zaten hedef durumda — veri değişmedi (tekrarlanan çağrı).</summary>
    Unchanged,
    NotFound,
    /// <summary>Mevcut durumdan bu duruma geçilemez (ör. iptal edilmiş sipariş kargolanamaz).</summary>
    InvalidTransition
}

/// <param name="Order">Geçişten sonraki (ya da geçiş olmadıysa mevcut) sipariş; bulunamadıysa null.</param>
public sealed record OrderStatusUpdateResult(OrderStatusChange Change, OrderInfo? Order);

/// <summary>
/// Sipariş yönetimi için secondary port.
/// </summary>
public interface IOrderRepository
{
    /// <summary>Yeni sipariş oluşturur ve oluşturulan sipariş ID'sini döner.</summary>
    string Create(OrderInfo order);

    /// <summary>
    /// Stoğu düşer ve siparişi yazar — <b>tek transaction</b> içinde.
    ///
    /// <para>
    /// Ayrı ayrı yapıldığında (önce düş, sonra yaz) ikisinin arasında oluşan herhangi bir hata
    /// stoğu düşülmüş ama karşılığında hiçbir sipariş oluşmamış hâlde bırakır. Hiçbir yerde
    /// hata görünmez; ürün stoğu sessizce ve kalıcı olarak azalır. Bu yüzden sipariş verme
    /// bölünemez bir işlemdir ve bu metot onu öyle temsil eder.
    /// </para>
    /// </summary>
    OrderPlacementResult PlaceOrder(OrderInfo order);

    /// <summary>Sipariş ID ile sorgular. Bulunamazsa null döner.</summary>
    OrderInfo? Get(string orderId);

    /// <summary>Müşterinin tüm siparişleri.</summary>
    IReadOnlyList<(string OrderId, OrderInfo Order)> GetByCustomer(string customerId);

    /// <summary>Müşterinin en son siparişi. Yoksa null döner.</summary>
    (string OrderId, OrderInfo Order)? GetLast(string customerId);

    /// <summary>Siparişi iptal eder. Başarılıysa true döner; iptal edilemez durumdaysa false.</summary>
    bool Cancel(string orderId, string reason);

    /// <summary>Sipariş için iade talebi oluşturur. Başarılıysa true döner; iade uygun değilse false.</summary>
    bool RequestReturn(string orderId, string reason);

    /// <summary>
    /// <c>İşleniyor → Kargolandı</c> (kargo firması/takip no isteğe bağlı). Atomik koşullu güncelleme: eşzamanlı
    /// iptal ile yarışta yalnızca biri kazanır.
    /// </summary>
    OrderStatusUpdateResult MarkShipped(string orderId, string? carrier, string? trackingNumber, DateTime shippedAtUtc);

    /// <summary><c>Kargolandı → Teslim Edildi</c>; <c>DeliveredAt</c> yazılır (iade süresi buradan sayılır).</summary>
    OrderStatusUpdateResult MarkDelivered(string orderId, DateTime deliveredAtUtc);
}
