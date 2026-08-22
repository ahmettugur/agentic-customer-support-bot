# IContextSanitizer

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IContextSanitizer.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IContextSanitizer`, <summary> Retrieval'dan (vector store, KB, lesson) gelen ham metni prompt'a girmeden önce güvenli hale getiren port. Prompt-injection yüzeyini daraltmak için iki katman sunar: yazma tarafında <see cref="Sanitize"/>, okuma tarafında <see cref="WrapRetrieved"/>. </summary> <summary> Ham metni temizler: newline dışındaki C0/C1 kontrol karakterlerini siler, HTML comment bloklarını (<c>&lt;!-- ... --&gt;</c>) kaldırır, <paramref name="maxLength"/> üzerindeki içeriği kırpar. </summary> <summary> Önce <see cref="Sanitize"/> uygular, sonra içeriği işaretli bir sarmalayıcıya alır: <c>&lt;retrieved_data source="..."&gt;…&lt;/retrieved_data&gt;</c>. İçerikte kapanış etiketi (case-insensitive) geçiyorsa fence kırılamaması için nötralize edilir. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IContextSanitizer`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
