# EntityVerifier

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/EntityVerifier.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## Ne işe yarar?

`EntityVerifier`, Application/Services/EntityVerifier.cs ReAct-lite entity grounding. IdExtractor regex ile formatı doğrular. EntityVerifier aşağıdakileri yapar: 1) Query'den extract et (IdExtractor) 2) History'den eksik olanları tamamla (önceki turlardaki entity'leri hatırla) 3) SessionState'ten tamamla (session.State.AuthenticatedCustomerId — JWT'den, poisonable değil) 4) DB ile varlık doğrulaması yap (IOrderRepository / IComplaintRepository üzerinden) 5) Türetilmiş alanları hesapla (ör. customer_id'den last_order_id)  Çıktı VerifiedEntities olarak reasoning prompt'una enjekte edilir. Böylece reasoning modeli "zaten bilinen bilgi için clarification isteme" kararını Tahmin üzerinden değil, grounded doğrulama üzerinden verir. <summary> Query + history + session state üzerinde deterministic entity çıkarımı yapar Ve repository port'ları ile doğrulayarak yapılandırılmış bir sonuç döner. Hiçbir LLM çağrısı yapmaz — tamamen deterministik. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`EntityVerifier`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public EntityVerifier(IOrderRepository orders,
        IComplaintRepository complaints,
        ILogger<EntityVerifier> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Verify`
```csharp
public VerifiedEntities Verify(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `BuildPromptBlock`
```csharp
public static string? BuildPromptBlock(VerifiedEntities verified)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
