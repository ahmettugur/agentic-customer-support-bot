# GeneralChatClientAdapter

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Chat/GeneralChatClientAdapter.cs`
- **Tür:** `public sealed class : IGeneralChatClient`
- **Namespace:** `CustomerSupportBot.Adapters.AI.Chat`

## Ne işe yarar?

`GeneralChatClientAdapter`, Application katmanının talep ettiği [IGeneralChatClient](../../CustomerSupportBot.Application/Ports/Outbound/AI/IGeneralChatClient.md) portunu uygulayan; `Microsoft.Extensions.AI.IChatClient` nesnesini port sınırında sararak Domain'in [ConversationMessage](../../CustomerSupportBot.Domain/Model/ConversationMessage.md) tipleri ile MEAI'nin `ChatMessage` tipleri arasındaki iki yönlü dönüşümü gerçekleştiren adaptördür.

## Hangi amaçla kullanılır`?

Hexagonal mimari kuralları gereği Application ve Domain katmanlarının `Microsoft.Extensions.AI` kütüphanesine doğrudan bağımlı olmasını engellemek; özetleme (`ConversationSummaryProvider`), yanıt doğrulama (`GroundingEvaluator`) ve genel metin tamamlama taleplerini güvenle karşılamak için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `IGeneralChatClient.CompleteAsync` sözleşmesini karşılamak.
  - `ConversationMessage.Role` dizgilerini (`user`, `system`, `assistant`) `Microsoft.Extensions.AI.ChatRole` enum türüne dönüştürmek (`RoleFor`).
  - LLM çağrısı sırasında oluşan teknik istisnaları [ExceptionTranslator](../ExceptionTranslator.md) ile yakalamak.

## Constructor ve Başlatma Mantığı

```csharp
public GeneralChatClientAdapter(IChatClient client)
```

### Constructor İçerisinde Yapılan İşler:
- `client` (`IChatClient`): MEAI chat client bağımlılığı `_client` alanına atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `CompleteAsync`
```csharp
public async Task<string> CompleteAsync(
    IReadOnlyList<ConversationMessage> messages,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Verilen mesaj listesini LLM'e iletip metin yanıtını döner.
- **İç Mantığı:**
  1. `messages.Select(m => new ChatMessage(RoleFor(m.Role), m.Text))` ile MEAI formatına haritalanır.
  2. `_client.GetResponseAsync(chatMessages, cancellationToken: ct)` çağrısı yapılır.
  3. `response.Text ?? ""` sonucu döndürülür.
  4. Hata durumunda `ExceptionTranslator.Translate(ex, "GeneralChatClient.CompleteAsync başarısız.")` ile `ExternalServiceException` fırlatılır.

### 2. `RoleFor` (Private Static)
```csharp
private static ChatRole RoleFor(string role)
```
- **Ne işe yarar?:** Domain rol dizgisini MEAI `ChatRole` nesnesine çevirir (`user` ➔ `ChatRole.User`, `system` ➔ `ChatRole.System`, diğer ➔ `ChatRole.Assistant`).

## Bağımlılıklar

- [IGeneralChatClient](../../CustomerSupportBot.Application/Ports/Outbound/AI/IGeneralChatClient.md)
- `Microsoft.Extensions.AI.IChatClient`
- [ExceptionTranslator](../ExceptionTranslator.md)
- [ConversationMessage](../../CustomerSupportBot.Domain/Model/ConversationMessage.md)
