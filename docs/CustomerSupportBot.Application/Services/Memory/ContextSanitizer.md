# ContextSanitizer

**Dosya:** `Services/Memory/ContextSanitizer.cs`
**Port:** `IContextSanitizer`
**Namespace:** `CustomerSupportBot.Application.Services.Memory`

## 1. Ne İşe Yarar

Vektör bellekten/bilgi bankasından geri getirilen (retrieval) metni, prompt'a eklenmeden önce
**prompt-injection saldırılarına karşı sertleştirir**: HTML yorumlarını ve kontrol karakterlerini
temizler, uzunluğu sınırlar, ve okunan içeriği modelin talimat olarak yorumlamasını zorlaştıran
bir "fence" (`<retrieved_data>`) içine sarar.

## 2. Hangi Amaçla Kullanılır

İki farklı kullanım yolu vardır:
- **Yazma tarafı (`Sanitize`):** Episodik belleğe (konuşma özetleri, müşteri profili çıkarımları)
  bir şey yazılmadan önce temizlik yapılır.
- **Okuma tarafı (`WrapRetrieved`):** Bilgi bankası (KB) veya onaylı ders (Lesson) içeriği
  prompt'a eklenmeden hemen önce, kaynağı belirten bir etiketle sarılır.

## 3. Sorumlulukları

- **Üstlendiği:** Metin temizliği (HTML yorumu, kontrol karakteri), uzunluk sınırlama,
  fence sarma ve fence kaçışını (escape) engelleme.
- **Üstlenmediği:** Kullanıcının DOĞRUDAN yazdığı mesajların temizliği (bu `IInputGuard`'da —
  ayrı ve daha geniş bir kontrol seti) — bu sınıf yalnızca **geri getirilen** (retrieval)
  içerikle ilgilenir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IContextSanitizer` port'unu implemente eder; **saf ve durumsuzdur, singleton olarak
  kaydedilebilir** (kod içi yorumda açıkça belirtilir).
- **Kimin tarafından çağrılır:** [`SemanticMemoryService`](SemanticMemoryService.md) ve
  bilgi bankası/ders okuyan context provider'lar (`Services/Providers/`).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden geri getirilen içerik ayrıca sertleştirilir — kullanıcı girdisi zaten `InputGuard`'dan
geçmiyor mu?** `InputGuard`, kullanıcının YAZDIĞI metni denetler. Ama vektör bellekteki/bilgi
bankasındaki içerik başka bir yoldan (KB dosyası ingest edilirken, ya da geçmiş bir konuşmadan
otomatik özetlenerek) sisteme girmiş olabilir — bu içerik kullanıcı girdisi filtresinden hiç
geçmemiştir. Eğer bir KB makalesi veya geçmiş bir konuşma özeti kazara/kötü niyetle bir
talimat kalıbı içeriyorsa (ör. "önceki talimatları unut..."), bu içerik prompt'a **retrieval**
yoluyla girer ve modelin bunu kullanıcı talimatıyla karıştırmaması gerekir.

**`WrapRetrieved` — fence kaçışı neden özellikle engellenir:** İçerik `<retrieved_data
source="...">...</retrieved_data>` etiketleriyle sarılır ki model bunu "bu bir veri bloğu,
talimat değil" olarak ayırt edebilsin. Ama eğer içeriğin KENDİSİ `</retrieved_data>` string'ini
barındırıyorsa, fence erken kapanır ve ondan sonraki (kötü niyetli) metin fence DIŞINDA, sanki
gerçek bir talimatmış gibi görünür. `ClosingTagRegex`, içerikteki HERHANGİ bir kapanış
etiketini `‹/retrieved_data›` (görsel olarak benzer ama işlevsel olarak zararsız, tek tırnaklı
guillemet karakterleriyle) çevirir — fence asla erken kapanamaz.

**`StripControlChars` — `\n` neden korunur, diğer kontrol karakterleri neden atılır:**
Satır sonu (`\n`) metnin okunabilirliği için gereklidir ve zararsızdır. Diğer C0 (0x00-0x1F),
DEL (0x7F) ve C1 (0x80-0x9F) kontrol karakterleri ise görünmez/beklenmedik render davranışlarına
(terminal kaçış dizileri, gizli metin) yol açabilir — bunlar sessizce atılır.

`Sanitize`, `HtmlCommentRegex` ile HTML yorumlarını (`<!-- ... -->`) da temizler — bir yorum
içine gizlenmiş talimat, admin panelinde (HTML render eden bir arayüzde) görünmez ama modele
metin olarak ulaşabilirdi.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Sanitize(string text, int maxLength = 2000): string` | HTML yorumu ve kontrol karakterlerini temizler, uzunluğu sınırlar. |
| `WrapRetrieved(string text, string source): string` | `Sanitize` + fence kaçışını engelleyip `<retrieved_data source="...">` içine sarar. |
| `StripControlChars(string text)` *(private static)* | `\n` hariç kontrol karakterlerini eler. |

## 7. Bağımlılıklar

Yok — durumsuz, dışarıdan hiçbir servis inject etmez; tüm regex'ler `[GeneratedRegex]` ile derleme-zamanı üretilir.

## Bağlantılar

- [SemanticMemoryService.md](SemanticMemoryService.md) — bu sanitizer'ı kullanan ana servis
- [../Chat/InputGuard.md](../Chat/InputGuard.md) — kullanıcı girdisi için ayrı, paralel bir kontrol
