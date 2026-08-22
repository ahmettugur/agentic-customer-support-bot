# SemanticMemoryContextProvider

- **Kaynak:** `Services/Providers/SemanticMemoryContextProvider.cs`
- **Tür:** `public sealed class : IContextProvider`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## 1. Ne İşe Yarar

Kullanıcının **bu turdaki** mesajını bir kez embed edip **üç ayrı** vektör koleksiyonunda
(Knowledge, Episodic, Lesson) paralel arama yapan, bulunan sonuçları bütçe sınırı içinde
markdown bloklarına dönüştüren sağlayıcıdır. `SemanticMemoryService.Enabled=false` ise
(bellek kapalıysa) hiç çalışmaz.

## 2. Hangi Amaçla Kullanılır

Ajanların şirket politikaları/SSS (Knowledge), bu müşteriyle **geçmiş oturumlardaki**
konuşmalar (Episodic) ve admin onaylı öğrenilmiş dersler (Lesson) hakkında halüsinasyon
üretmeden, gerçek veriye dayalı yanıt vermesini sağlamak. `IContextSanitizer` ile vektör
ambarından gelen harici metinler prompt enjeksiyonuna karşı temizlenir.

## 3. Sorumlulukları

**Üstlendiği:**
- Sorguyu **bir kez** embed edip üç `SearchByVectorAsync` çağrısını paralel (`Task.WhenAll`)
  çalıştırmak.
- Doğrulanmış müşteri kimliği varsa (`session.State.AuthenticatedCustomerId`), Episodic
  aramasını o müşterinin `customerId` etiketiyle filtrelemek.
- Karakter bütçesi (`_memory.Options.Retrieval.MaxContextChars`) dahilinde sonuçları kırpmak.
- Tüm hataları yutup `null` dönmek — bu provider **iyileştirici**dir (`IsCritical` yok/false),
  düşerse tur yine de devam eder.

**Üstlenmediği:** Embedding/arama altyapısı (`SemanticMemoryService`'in işi), metin
sterilizasyonu (`IContextSanitizer`'ın işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `SemanticMemoryService` (Services/Memory) — embed + vektör arama.
- `IContextSanitizer` — başlık ve içerik metinlerini sterilize eder, retrieved-content'i
  wrap eder (`WrapRetrieved`).
- [`ContextPipeline`](../Chat/ContextPipeline.md) — bu provider'ı `Order=7` ile çalıştıran
  tüketici; ağ/LLM çağrısı içerdiği için pipeline'ın per-provider timeout mekanizmasına tabidir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Geçmişte iki kez embed ediliyordu:** Eskiden Knowledge ve Lesson için iki ayrı
> `SearchAsync` çağrısı vardı ve her biri aynı metni kendi içinde yeniden embed ediyordu —
> embedder'da cache olmadığı için tur başına iki embedding çağrısı (iki kat maliyet)
> oluşuyordu. Artık sorgu **bir kez** embed edilip aynı vektörle üç koleksiyonda da aranıyor.

> 🐞 **Episodic bellek write-only ölü veriydi:** `WriteEpisodeAsync` her turda bir kayıt
> üretiyordu ama `SearchAsync(MemoryKind.Episodic, …)` kod tabanında hiçbir yerde
> çağrılmıyordu. Bu provider, doğrulanmış müşteri kimliği varsa episode'ları `customerId`
> tag'iyle (session ID'siyle değil — aksi halde aynı müşterinin dünkü ve bugünkü oturumu
> birbirine hiç bağlanamazdı) arayarak bu ölü veriyi canlandırdı.

`currentQuery`'nin neden ayrı bir parametre olarak geldiği (oturum geçmişinden değil) için
bkz. [IContextProvider.md](IContextProvider.md) — aynı kök nedenin bu sınıftaki somut sonucu.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Name` (`string`) | Sabit `"SemanticMemory"`. |
| `Order` (`int`) | `7` — `CustomerProfileContextProvider` (6) sonrası, `ProductRecommendationContextProvider` (8) öncesi. |
| `GetContextAsync(session, currentQuery, ct)` | `_memory.Enabled` değilse veya sorgu boşsa `null`. Aksi halde: sorguyu embed eder → 3 koleksiyonu paralel arar (Episodic sadece kimlik doğrulanmışsa) → hiçbiri sonuç vermezse `null` → varsa `"## 📚 İlgili Bilgi Tabanı"`, `"## 🗂️ Bu Müşteriyle Geçmiş Görüşmeler"`, `"## 🎓 Geçmiş Derslerden Öğrenilenler"` bölümlerini bütçe sırasıyla doldurur. `try/catch` içinde — arama başarısız olursa loglanır ve `null` döner (tur bloklanmaz). |
| `AppendHits(sb, hits, ref budget)` *(private)* | Her sonucu `- **[başlık]** _(score=…, kaynak=…)_` satırı + sanitize edilmiş/kırpılmış/wrap'lenmiş içerik olarak ekler; `budget <= 100` olunca durur. Sırasıyla: sanitize → bütçe kırpması → wrap (fence hiçbir zaman bölünmez). |

## 7. Bağımlılıklar

Constructor injection ile: `SemanticMemoryService`, `IContextSanitizer`, `ILogger<SemanticMemoryContextProvider>`.

## Bağlantılar

- [IContextProvider.md](IContextProvider.md)
- [../Memory/SemanticMemoryService.md](../Memory/SemanticMemoryService.md)
- [../Memory/ContextSanitizer.md](../Memory/ContextSanitizer.md)
