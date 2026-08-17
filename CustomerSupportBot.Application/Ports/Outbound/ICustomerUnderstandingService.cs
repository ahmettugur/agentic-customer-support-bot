using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Memory'nin üç kaynağını (yapısal olgular, profil, çıkarımlar) tek bir
/// <see cref="CustomerUnderstanding"/> nesnesinde sentezler.
///
/// <para>
/// Tek doğruluk kaynağı olması bilinçli: sentez mantığı (null-kontrolleri, "tur sıfırsa
/// gösterme" kuralı, sıralama) burada bir kez yazılır; hem bugünkü tüketici
/// (<c>CustomerProfileContextProvider</c>) hem de ileride eklenecek bir öneri motoru aynı
/// kuralları iki kez uygulamak zorunda kalmaz.
/// </para>
/// </summary>
public interface ICustomerUnderstandingService
{
    /// <summary>
    /// Oturumun doğrulanmış müşteri kimliği yoksa, profili yoksa veya profil hiç etkileşim
    /// görmemişse (<c>TotalTurns == 0</c>) <c>null</c> döner — "gösterilecek bir şey yok"
    /// ile "bilgi var ama boş" ayrımını çağırana bırakmaz.
    /// </summary>
    CustomerUnderstanding? Build(AgentSession session);
}
