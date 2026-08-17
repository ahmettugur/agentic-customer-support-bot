using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Providers;

public interface IContextProvider
{
    string Name { get; }
    int Order { get; }

    /// <summary>
    /// Bu provider'ın katkısı <b>doğru cevap için gerekli mi</b>, yoksa yalnızca iyileştirici mi?
    ///
    /// <para>
    /// Ayrım, hata anında ne yapılacağını belirler. İyileştirici bir provider düşerse (ör.
    /// semantik bellek erişilemiyor) bot yine makul bir cevap verebilir; sessizce atlamak
    /// doğrudur. Kritik bir provider düşerse — ör. müşterinin sipariş geçmişi okunamıyorsa —
    /// model eksikliği FARK ETMEZ ve büyük olasılıkla "kayıtlı siparişiniz bulunamadı" der:
    /// altyapı hatası kullanıcıya <b>yanlış olgu</b> olarak yansır.
    /// </para>
    ///
    /// <para>
    /// Bu yüzden kritik bir provider düştüğünde pipeline sessiz kalmaz; bağlama açık bir
    /// "bu bilgi şu an okunamıyor, bilmiyorum de" uyarısı koyar. Tur yine tamamlanır ama
    /// model uydurmak yerine bilmediğini söyler.
    /// </para>
    /// </summary>
    bool IsCritical => false;

    /// <summary>
    /// Bu tur için bağlam metni üretir.
    /// </summary>
    /// <param name="session">Oturum — kalıcı durum ve kimlik bilgisi için.</param>
    /// <param name="currentQuery">
    /// Kullanıcının <b>bu turdaki</b> mesajı.
    ///
    /// <para>
    /// Ayrı bir parametre olarak geçilir çünkü oturum geçmişinden okunamaz: geçmiş
    /// (<c>AddExchange</c>/<c>PersistExchange</c>) workflow BİTTİKTEN sonra yazılır,
    /// dolayısıyla bağlam kurulurken güncel mesaj henüz geçmişte yoktur.
    /// </para>
    ///
    /// <para>
    /// Bu parametre yokken <c>SemanticMemoryContextProvider</c> aranacak metni geçmişteki
    /// son kullanıcı mesajından tahmin ediyordu; sonuç olarak ilk turda hiç retrieval
    /// yapılmıyor, sonraki turlarda ise arama BİR ÖNCEKİ turun sorusuyla yapılıyordu —
    /// hem bilgi tabanı hem onaylanmış dersler yanlış sorguyla getiriliyordu.
    /// </para>
    /// </param>
    /// <param name="ct">
    /// Provider başına zaman aşımı ve çağıran iptalinin birleşimi. Ağ/LLM çağrısı yapan her
    /// provider bunu aşağıya geçirmelidir — aksi halde süre dolsa bile iş arka planda sürer.
    /// </param>
    Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default);
}
