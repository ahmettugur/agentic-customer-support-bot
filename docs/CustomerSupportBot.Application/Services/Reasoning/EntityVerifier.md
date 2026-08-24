# EntityVerifier

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/EntityVerifier.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## Ne işe yarar?

`EntityVerifier`, query + history + authenticated session kaynaklarını birleştiren deterministik
entity resolver'dır. `IdExtractor` ile ID adaylarını çıkarır, bağlamsız takip mesajlarını önceki
konuşma bağlamına göre sınıflandırır ve müşteri kimliğini yalnızca
`SessionState.AuthenticatedCustomerId` üzerinden alır.

Sipariş/şikayet kaydının varlığı, sahipliği, durumu ve içeriği burada DB'den okunmaz. Bu
gerçekler yalnızca authenticated customer kimliğini kullanan specialist tool tarafından
doğrulanır. Böylece her turda eager repository sorgusu yapılmaz ve reasoning prompt'una yanlış
veya başka müşteriye ait iş verisi taşınamaz.

## Hangi amaçla kullanılır?

- Kullanıcının sağladığı ID'yi tekrar istemeden reasoning/workflow'a taşımak.
- Kısa takip mesajlarında query + history bağlam sürekliliğini korumak.
- Kullanıcının metinde yazdığı customer ID'nin authenticated kimliği ezmesini engellemek.
- Factual doğrulamayı sahiplik kontrollü tool katmanına ertelemek.

## Sorumlulukları

- **Üstlendiği:** Entity extraction, context continuity, kaynak önceliği ve güvenli prompt bloğu.
- **Üstlenmediği:** Sipariş/şikayet DB lookup'ı, sahiplik kontrolü veya iş verisi üretmek.

## Constructor ve Başlatma Mantığı

```csharp
public EntityVerifier(ILogger<EntityVerifier> logger)
```
- Repository bağımlılığı yoktur; singleton olarak güvenle kullanılabilir.

## Metotlar ve İç Çalışma Mantıkları

### `Verify`
```csharp
public VerifiedEntities Verify(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null)
```
- `order_id` ve `complaint_id` değerlerini `FormatOnly` olarak döndürür. Bu değerlerin varlığı ve
  sahipliği specialist tool'da doğrulanır.
- `customer_id` yalnız authenticated session'dan gelir ve güvenilir sistem kaynağı olarak
  `Verified` işaretlenir.
- `DerivedLastOrderId`, `DerivedOrderCount` ve DB attribute'ları üretilmez.

### `BuildPromptBlock`
```csharp
public static string? BuildPromptBlock(VerifiedEntities verified)
```
- `[RESOLVED ENTITIES]` bloğunu üretir. `FormatOnly` değerlerin tool ile doğrulanmadan gerçek
  kabul edilmemesi gerektiğini prompt'a açıkça yazar.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
