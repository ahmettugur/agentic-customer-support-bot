namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Retrieval'dan (vector store, KB, lesson) gelen ham metni prompt'a girmeden önce
/// güvenli hale getiren port. Prompt-injection yüzeyini daraltmak için iki katman sunar:
/// yazma tarafında <see cref="Sanitize"/>, okuma tarafında <see cref="WrapRetrieved"/>.
/// </summary>
public interface IContextSanitizer
{
    /// <summary>
    /// Ham metni temizler: newline dışındaki C0/C1 kontrol karakterlerini siler,
    /// HTML comment bloklarını (<c>&lt;!-- ... --&gt;</c>) kaldırır, <paramref name="maxLength"/>
    /// üzerindeki içeriği kırpar.
    /// </summary>
    string Sanitize(string text, int maxLength = 2000);

    /// <summary>
    /// Önce <see cref="Sanitize"/> uygular, sonra içeriği işaretli bir sarmalayıcıya alır:
    /// <c>&lt;retrieved_data source="..."&gt;…&lt;/retrieved_data&gt;</c>.
    /// İçerikte kapanış etiketi (case-insensitive) geçiyorsa fence kırılamaması için nötralize edilir.
    /// </summary>
    string WrapRetrieved(string text, string source);
}
