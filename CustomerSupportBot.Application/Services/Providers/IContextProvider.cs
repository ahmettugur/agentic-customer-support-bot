using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Providers;

public interface IContextProvider
{
    string Name { get; }
    int Order { get; }

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
    Task<string?> GetContextAsync(AgentSession session, string currentQuery);
}
