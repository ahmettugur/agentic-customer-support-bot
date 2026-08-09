# KnowledgeApiService

## Ne İşe Yarar
Bilgi tabanı (Knowledge Base) CRUD işlemlerini backend `/memory/articles` endpoint'leri üzerinden gerçekleştiren HTTP istemci servisidir.

## Hangi Amaçla Kullanılır
`Knowledge.razor` sayfasında makale listeleme, oluşturma, güncelleme ve silme işlemlerinde kullanılır.

## Sorumlulukları
- Makale listesi çekme (okuma yolunda hata yutma).
- Yeni makale oluşturma (yazma yolunda hata fırlatma).
- Mevcut makaleyi güncelleme.
- Makale silme.
- Sunucu hata kodlarını okunabilir Türkçe mesajlara çevirme.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `HttpClient`.
- **Kullanan bileşen**: `Pages/Knowledge.razor`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → `MemoryEndpoints`.
- **Model bağımlılığı**: [KnowledgeModels](../Models/KnowledgeModels.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Okuma/yazma yolları farklı hata stratejileri kullanır: okuma yolunda UI çökmemesi için hata yutulur; yazma yollarında kullanıcının sonucu görmesi gerektiğinden `InvalidOperationException` fırlatılır. Hata kodları (`title_required`, `content_required` vb.) sunucudan alınıp switch expression ile Türkçe'ye çevrilir.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `ListAsync()` | Tüm makaleleri listeler. |
| `CreateAsync(input)` | Yeni makale oluşturur; `KnowledgeArticleSaveDto` döner. |
| `UpdateAsync(id, input)` | Mevcut makaleyi günceller. |
| `DeleteAsync(id)` | Makaleyi siler. |
| `DescribeErrorAsync(response)` | HTTP hata kodunu Türkçe mesaja çevirir (private). |
| `ReadErrorCodeAsync(response)` | JSON body'den `error` alanını okur (private). |

## Bağımlılıklar
- `HttpClient` — Bearer token zincirli.
- [KnowledgeModels](../Models/KnowledgeModels.md) — `KnowledgeArticleDto`, `KnowledgeArticleInput`, `KnowledgeArticleSaveDto`.
