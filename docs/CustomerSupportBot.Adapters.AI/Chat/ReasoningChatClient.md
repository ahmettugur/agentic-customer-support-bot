# ReasoningChatClient

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Chat/ReasoningChatClient.cs`
- **Tür:** `public class : IReasoningChatClient`
- **Namespace:** `CustomerSupportBot.Adapters.AI.Chat`

## Ne işe yarar?

`ReasoningChatClient`, Application katmanındaki [IReasoningChatClient](../../CustomerSupportBot.Application/Ports/Outbound/AI/IReasoningChatClient.md) portunu uygulayan; OpenAI/Azure OpenAI o-serisi (o1, o3-mini vb.) akıl yürütme modellerini `reasoning_effort` parametresiyle çağıran, hem tam metin (`CompleteAsync`) hem de anlık token akışı (`StreamAsync`) sunan muhakeme adaptörüdür.

## Hangi amaçla kullanılır`?

- [ReasoningService](../../CustomerSupportBot.Application/Services/Reasoning/ReasoningService.md) tarafından tetiklenen derin niyet analizi, entity çıkarımı ve alt görev bölme çağrılarını yürütmek.
- `ChatOptions.AdditionalProperties` içerisine `reasoning_effort` ("low", "medium", "high") enjekte ederek modelin akıl yürütme derinliğini yapılandırmak.
- [ConversationMessage](../../CustomerSupportBot.Domain/Model/ConversationMessage.md) domain tipleri ile `Microsoft.Extensions.AI.ChatMessage` arasında çift yönlü dönüşüm sağlamak.

## Sorumlulukları

- **Üstlendiği:**
  - `IReasoningChatClient` sözleşmesindeki `CompleteAsync` ve `StreamAsync` metotlarını karşılamak.
  - `BuildOptions` ile `reasoning_effort` parametresini `ChatOptions` nesnesine eklemek.
  - `Map` ve `RoleFor` ile domain mesajlarını MEAI formatına dönüştürmek.
  - Hataları [ExceptionTranslator](../ExceptionTranslator.md) ile domain istisnasına çevirmek.

## Constructor ve Başlatma Mantığı

```csharp
public ReasoningChatClient(
    IChatClient client,
    string modelName,
    string reasoningEffort)
```

### Constructor İçerisinde Yapılan İşler:
- `client` (`IChatClient`): MEAI chat client bağımlılığı `_client` alanına atanır.
- `modelName` (`string`): Kullanılan muhakeme modelinin adı `ModelName` özelliğine atanır.
- `reasoningEffort` (`string`): Muhakeme efor seviyesi (`low`, `medium`, `high`) `ReasoningEffort` özelliğine atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `CompleteAsync`
```csharp
public async Task<string> CompleteAsync(
    IReadOnlyList<ConversationMessage> messages,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Muhakeme modeline mesajları ileterek tek seferde tam metin yanıtı döner.
- **İç Mantığı:**
  1. `Map(messages)` ile mesajlar dönüştürülür.
  2. `BuildOptions()` ile `reasoning_effort` eklenir.
  3. `_client.GetResponseAsync(chatMessages, options, ct)` çağrılır ve `response.Text ?? ""` döner.
  4. Hata durumunda `ExceptionTranslator.Translate` işletilir.

### 2. `StreamAsync`
```csharp
public async IAsyncEnumerable<string> StreamAsync(
    IReadOnlyList<ConversationMessage> messages,
    [EnumeratorCancellation] CancellationToken ct = default)
```
- **Ne işe yarar?:** Muhakeme modelinden anlık olarak akan düşünce/yanıt parçalarını stream eder.
- **İç Mantığı:** `_client.GetStreamingResponseAsync(chatMessages, options, ct)` çağrılır; gelen `ChatResponseUpdate.Text` parçaları `yield return` ile yayınlanır.

### 3. `BuildOptions` (Private)
```csharp
private ChatOptions BuildOptions() => new()
{
    AdditionalProperties = new AdditionalPropertiesDictionary
    {
        [WellKnown.ReasoningEffort.PropertyKey] = ReasoningEffort
    }
};
```
- **Ne işe yarar?:** o-serisi modellerin beklediği `reasoning_effort` parametresini MEAI opsiyonlarına ekler.

## Özellikler (Properties)

| Özellik | Tür | Açıklama |
|---|---|---|
| `ModelName` | `string` | Kullanılan aktif muhakeme modeli adı (ör. `o3-mini`). |
| `ReasoningEffort` | `string` | Uygulanan efor seviyesi (`low`, `medium`, `high`). |

## Bağımlılıklar

- [IReasoningChatClient](../../CustomerSupportBot.Application/Ports/Outbound/AI/IReasoningChatClient.md)
- `Microsoft.Extensions.AI.IChatClient`
- [ExceptionTranslator](../ExceptionTranslator.md)
- [ConversationMessage](../../CustomerSupportBot.Domain/Model/ConversationMessage.md)
