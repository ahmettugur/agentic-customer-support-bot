# ContextSanitizer

## Ne İşe Yarar
Retrieval (semantik bellek) içeriğini prompt injection'a karşı sertleştiren güvenlik servisidir.

## Hangi Amaçla Kullanılır
Yazma tarafında episodik bellek kayıtlarını temizlemek (`Sanitize`), okuma tarafında KB/Lesson sonuçlarını güvenli `<retrieved_data>` fence'i ile sarmak (`WrapRetrieved`) için kullanılır.

## Sorumlulukları
- HTML yorumlarını (`<!-- ... -->`) temizlemek.
- Kontrol karakterlerini (C0, DEL, C1) kaldırmak (newline korunur).
- Maksimum uzunluk sınırı uygulamak.
- `</retrieved_data>` kapanış etiketini nötralize etmek — fence kırılmasını önlemek.
- İçeriği `<retrieved_data source="...">...</retrieved_data>` fence'i ile sarmak.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Port arayüzü**: `IContextSanitizer` (Outbound port).
- **Kullanan sınıflar**: `SemanticMemoryContextProvider`, `SemanticMemoryService`.
- **DI kaydı**: Singleton olarak `ApplicationServiceCollectionExtensions`'da.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Saf ve stateless — hiçbir I/O yapmaz, singleton olarak kaydedilebilir. Fence kapanış etiketinin içerikte geçmesi durumunda single guillemet'e (`‹`, `›`) dönüştürülür — böylece fence asla kırılamaz ve prompt injection engellenmiş olur.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `Sanitize(text, maxLength)` | HTML yorum ve kontrol karakterlerini temizler, uzunluk sınırı uygular. |
| `WrapRetrieved(text, source)` | Temizlenmiş metni `<retrieved_data>` fence'i ile sarar. |

## Bağımlılıklar
Yok — saf C# (System.Text, System.Text.RegularExpressions).
