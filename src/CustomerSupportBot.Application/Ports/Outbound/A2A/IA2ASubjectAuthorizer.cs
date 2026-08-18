// Application/Ports/Outbound/A2A/IA2ASubjectAuthorizer.cs
// "Bu partner, bu müşteri adına işlem yapabilir mi?" kararının portu.

namespace CustomerSupportBot.Application.Ports.Outbound.A2A;

/// <summary>
/// A2A token değişiminde sorulan tek soru: <b>partner P, müşteri C adına hareket edebilir mi?</b>
///
/// <para>
/// <b>Neden ayrı bir port:</b> bu bir teknik kural değil, <b>iş kuralıdır</b>. "Hangi partner
/// hangi müşteriye erişebilir" sorusunun cevabı işletmeye göre değişir — partnerin kendi
/// tanıttığı müşteriler, bir sözleşme kapsamı, bir bayi hiyerarşisi olabilir. Bu kararı token
/// değişimi mantığının içine gömmek, ileride değiştirilemez hâle getirirdi.
/// </para>
///
/// <para>
/// ⚠️ <b>Bu kontrol atlanırsa ne olur:</b> partner token'ı ele geçiren biri, herhangi bir
/// müşteri numarasını isteyerek O müşterinin sipariş geçmişine erişebilir. Yani bu port,
/// A2A kanalındaki <b>tek müşteri-bazlı yetki sınırıdır</b>; tool katmanındaki sahiplik
/// kontrolü ondan sonra gelir ve onun yerini tutmaz (o, "token'daki müşteriye ait mi"
/// sorusunu sorar — token'ın doğru müşteriye ait olduğunu varsayar).
/// </para>
/// </summary>
public interface IA2ASubjectAuthorizer
{
    /// <summary>
    /// <paramref name="partnerId"/>'nin <paramref name="customerId"/> adına token alabilmesine
    /// izin var mı? Belirsizlik hâlinde <b>false</b> dönmelidir — bu kapı yanlış açıldığında
    /// bedeli başka bir müşterinin verisidir.
    /// </summary>
    Task<bool> CanActForCustomerAsync(string partnerId, string customerId, CancellationToken ct = default);
}
