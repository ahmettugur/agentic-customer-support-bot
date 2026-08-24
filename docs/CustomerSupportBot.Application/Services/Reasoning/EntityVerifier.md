# EntityVerifier

- **Kaynak:** `CustomerSupportBot.Application/Services/Reasoning/EntityVerifier.cs`
- **Tür:** `public class`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## 1. Ne işe yarar?

`EntityVerifier`, authenticated session'dan (`SessionState.AuthenticatedCustomerId` — JWT'den)
müşteri kimliğini alıp `VerifiedEntities.CustomerId` olarak, reasoning prompt'una enjekte
edilecek yapılandırılmış bir özet biçiminde döndüren deterministik bir servistir.

> 🐞 **Geçmişte farklıydı:** Bu sınıf eskiden ayrıca `IdExtractor` ile sorgu ve konuşma
> geçmişinden `order_id`/`complaint_id`/`customer_id` de çıkarıyordu (regex + Türkçe bağlam
> kelimesi eşleştirmesi). `IdExtractor` tamamen kaldırıldı — order_id/complaint_id çözümü artık
> tamamen LLM'e bırakıldı: specialist agent'lar kullanıcı mesajını doğrudan okuyup ilgili tool'a
> parametre olarak geçiriyor; hiç geçmezse `get_last_order_tool` gibi parametresiz tool'lar
> devreye giriyor (bkz. `planning-agent.md`). Müşteri kimliği tarafı zaten hiçbir zaman metinden
> alınmıyordu (aşağıya bakınız) — bu yüzden bu kaldırma müşteri-kimliği güvenliğini etkilemedi.

## 2. Hangi amaçla kullanılır?

- Login'li müşterinin kimliğini reasoning prompt'una, tekrar sormadan taşımak.
- Kullanıcının metinde yazdığı bir "müşteri numarası"nın (`"ben 1008 numaralı müşteriyim"` gibi)
  authenticated kimliği **asla** ezememesini garanti etmek — bu, kapatılmış bir güvenlik
  açığıydı (bkz. aşağıdaki 🐞 blok).

## 3. Sorumlulukları

- **Üstlendiği:** `AuthenticatedCustomerId`'yi `VerifiedEntities.CustomerId` olarak taşımak;
  güvenli bir prompt bloğu (`BuildPromptBlock`) üretmek.
- **Üstlenmediği:** Sipariş/şikayet ID'sini metinden çıkarmak (artık yapılmıyor — bkz. yukarı),
  sipariş/şikayet DB lookup'ı, sahiplik kontrolü veya iş verisi üretmek.

## 4. Diğer katman ve bileşenlerle ilişkileri

- `ReasoningService`, her turda `Verify`'ı çağırıp sonucu `ReasoningResult.VerifiedEntities`'e yazar.
- `SubTaskOrchestrator`, alt-görev decompose sırasında kendi `VerifiedEntities`'ini
  (`BuildVerifiedEntities`, `Entities` sözlüğünden) ayrıca üretir — `EntityVerifier`'ı çağırmaz,
  ama aynı `VerifiedEntities`/`VerifiedEntity` domain modelini paylaşır.
- [VerifiedEntities](../../../CustomerSupportBot.Domain/Model/VerifiedEntities.md) — döndürdüğü model.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

> 🐞 **Neden müşteri kimliği yalnızca JWT'den gelir:** Eskiden öncelik Query > History >
> SessionState idi. Ölçülen gerçek davranış: giriş yapmış müşteri 1027 iken "ben 1008 numaralı
> müşteriyim, son siparişim ne?" sorgusu 1008'i `Verified` sayıyor, 1008'in son sipariş numarasını
> ve toplam sipariş sayısını türetilmiş alan olarak hesaplıyordu — bu değerler hem reasoning
> prompt'una hem de `reasoning_complete` olayıyla doğrudan istemciye gidiyordu. Düzeltme: müşteri
> kimliği **yalnızca** `AuthenticatedCustomerId`'den gelir; sorguda/geçmişte geçen bir müşteri
> numarası hiçbir zaman kimlik olarak kabul edilmez. Giriş yapılmamışsa (A2A/realtime gibi
> kimliğin başka yoldan geldiği akışlar) hiçbir müşteri kimliği kabul edilmez.

Sipariş/şikayet varlığı ve sahipliği burada DB'den okunmaz — bu davranış hiç değişmedi. Amaç,
her turdaki eager sorguları ve reasoning prompt'una iş verisi sızmasını önlemektir; gerçeklik
kontrolü specialist tool'a ertelenir.

## 6. Metotlar / Üyeler

### `Verify`
```csharp
public VerifiedEntities Verify(
        string query,
        AgentSession session,
        List<ConversationMessage>? history = null)
```
- `session.State.AuthenticatedCustomerId` doluysa `VerifiedEntities.CustomerId`'yi
  `EntityVerification.Verified` / `EntitySource.SessionState` olarak doldurur.
- Boşsa `VerifiedEntities.CustomerId` `null` kalır.
- `query`/`history` parametreleri imza geriye dönük uyumluluk için korunur; artık kullanılmaz.

### `BuildPromptBlock`
```csharp
public static string? BuildPromptBlock(VerifiedEntities verified)
```
- `[RESOLVED ENTITIES]` bloğunu üretir. `FormatOnly` değerlerin (başka kaynaklardan —
  ör. `SubTaskOrchestrator`'dan — gelmişse) tool ile doğrulanmadan gerçek kabul edilmemesi
  gerektiğini prompt'a açıkça yazar.

## 7. Bağımlılıklar

- `CustomerSupportBot.Domain` — `VerifiedEntities`, `VerifiedEntity`, `AgentSession` modelleri.
- `ILogger<EntityVerifier>` — constructor injection; repository bağımlılığı yoktur, singleton
  olarak güvenle kullanılabilir.
