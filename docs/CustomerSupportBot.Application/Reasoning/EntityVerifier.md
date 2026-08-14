# EntityVerifier

**Dosya:** `Services/Reasoning/EntityVerifier.cs`  
**Implements:** Yok (concrete class)  
**Yaşam döngüsü:** Singleton (DI)

## 1. Ne İşe Yarar

Kullanıcı sorgusu, konuşma geçmişi ve oturum state'inden entity ID'lerini (sipariş, müşteri, şikayet) çıkarır, DB'de doğrular ve türetilmiş alanları hesaplar. **Hiçbir LLM çağrısı yapmaz — tamamen deterministik.**

## 2. Hangi Amaçla Kullanılır

Reasoning Pipeline'ın **L0 (ilk) katmanı**dır. Her kullanıcı mesajında çalışır ve `VerifiedEntities` üretir. Bu sonuç reasoning LLM'ine "zaten doğrulanmış bilgi" olarak enjekte edilir — böylece LLM "siparişinizin numarasını alabilir miyim?" diye gereksiz soru sormaz.

> 💡 **Analiz notu:** Bir hastanede karşılama bankosunun TC kimlik doğrulaması gibi düşün. Hasta "TC 12345678901" dediğinde doktor hasta kayıtlarını açmadan önce bu TC'nin sistemde olup olmadığı doğrulanır. EntityVerifier de aynı şeyi yapar — "1030" diye bir sipariş var mı DB'den kontrol eder.

## 3. Sorumlulukları

- ✅ Query'den entity ID çıkarmak (IdExtractor ile)
- ✅ History'den eksik olanları tamamlamak
- ✅ SessionState.AuthenticatedCustomerId'den fallback sağlamak (**SessionState.CustomerId'den DEĞİL** — poisoning riski)
- ✅ DB ile varlık doğrulaması yapmak (IOrderRepository / IComplaintRepository)
- ✅ Türetilmiş alanları hesaplamak (DerivedLastOrderId, DerivedOrderCount)
- ❌ LLM çağrısı yapmak
- ❌ Prompt oluşturmak

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim çağırır:** `ReasoningService.ReasonAsync()` — pipeline'ın ilk adımı olarak
- **Kim kullanır çıktısını:**
  - `ReasoningMessageBuilder` — prompt'a verified entity bloğu enjekte eder
  - `ReasoningSanityChecker` — verified entities ile reasoning çıktısını karşılaştırır
  - `ReasoningResult.VerifiedEntities` — sonuç olarak taşınır

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **Güvenlik: AuthenticatedCustomerId vs CustomerId:** `SessionState.CustomerId` LLM'in kullanıcı metninden çıkardığı numaradır — kullanıcı "ben 1008 numaralı müşteriyim" diyerek başka birinin bilgilerini sızdırabilir (poisoning). `AuthenticatedCustomerId` ise JWT'den gelir ve güvenilirdir. Bu fark keşfedilip düzeltilmiştir — regresyon testi: `EntityVerifierTests.Verify_PoisonedSessionStateCustomerId_IsIgnored`.

> 💡 **Öncelik sırası kasıtlıdır:** Query > History > SessionState. En güncel ve en güvenilir kaynak query'dir; session state en düşük önceliklidir.

## 6. Metotlar

| Metot | İmza | Açıklama |
|-------|------|----------|
| `Verify` | `VerifiedEntities Verify(string query, AgentSession session, List<ConversationMessage>? history)` | Entity çıkar, doğrula, türet |
| `BuildPromptBlock` | `static string? BuildPromptBlock(VerifiedEntities)` | Prompt'a enjekte edilecek metin bloğu oluştur |

### Verify Akışı

```
1) Query'den regex ile ID çıkar (IdExtractor)
2) Bağlam devamlılığı uygula (ApplyContextContinuity)
3) History'den eksikleri tamamla (en yeniden en eskiye)
4) SessionState.AuthenticatedCustomerId fallback
5) Her entity için DB doğrulaması (Verified/NotFoundInDb/FormatOnly)
6) Customer verified ise türetilmiş alanlar hesapla
```

### Sipariş entity'sinin attribute'ları

`VerifyOrder`, doğrulanan sipariş için `VerifiedEntity.Attributes` sözlüğünü doldurur:

| Anahtar | Değer |
|---|---|
| `status` | Sipariş durumu |
| `product` | `OrderInfo.LinesSummary()` — çok satırlı siparişte `"Kahve x2, Çikolata x1"` |
| `quantity` | `OrderInfo.TotalQuantity()` — satırların adet toplamı |
| `customerId` | Sipariş sahibi |

`Attributes` düz `string → string` bir sözlüktür, yani satır **yapısı** buradan taşınamaz. Bu kabul edilebilir: bu alanların tek tüketicisi `BuildPromptBlock`, yani downstream prompt'a "hangi sipariş neyi içeriyor" bilgisini vermek. Makine tarafından okunması gereken taraf (`ResponseAgent`) bu özeti değil, `ToolResult.Data`'daki yapılandırılmış `lines` dizisini kullanır — bkz. [OrderToolsService](../Tools/OrderToolsService.md).

## 7. Constructor Bağımlılıkları

```csharp
public EntityVerifier(
    IOrderRepository orders,      // Sipariş DB sorguları
    IComplaintRepository complaints, // Şikayet DB sorguları
    ILogger<EntityVerifier> logger   // Loglama
)
```

## Bağlantılar

- [ReasoningService.md](ReasoningService.md) — Bu servisi çağıran orkestratör
- [ReasoningMessageBuilder.md](ReasoningMessageBuilder.md) — Prompt'a entity bloğu enjekte eden
- [ReasoningSanityChecker.md](ReasoningSanityChecker.md) — Entity ile reasoning çıktısını karşılaştıran
- [../../CustomerSupportBot.Domain/Model/VerifiedEntities.md](../../CustomerSupportBot.Domain/Model/VerifiedEntities.md) — Çıktı modeli
- [../../CustomerSupportBot.Domain/Services/IdExtractor.md](../../CustomerSupportBot.Domain/Services/IdExtractor.md) — Regex çıkarım
