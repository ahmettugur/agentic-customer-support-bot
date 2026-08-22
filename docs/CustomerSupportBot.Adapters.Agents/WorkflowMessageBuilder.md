# WorkflowMessageBuilder

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/WorkflowMessageBuilder.cs`
- **Tür:** `internal sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`WorkflowMessageBuilder`, Microsoft Agents Framework (MAF) grup sohbeti iş akışı başlatılmadan önce; context pipeline (RAG + episodik bellek), müşteri kimliği ipuçları, Reasoning özetleri, çıkarılan ID'ler (`ExtractedIds`), yönetici replan notları ve geçmiş konuşma mesajlarını sıralı ve optimize bir şekilde derleyerek `WorkflowPrompt` (mesajlar + bağlam durumu) üreten derleyicidir.

## Hangi amaçla kullanılır`?

- **Sistem İpuçlarının Doğru Sırada Enjeksiyonu:** Ajan prompt'larının beklediği formatta kimlik (`CustomerIdentityHint`), bağlam (`ContextResult`), muhakeme (`ReasoningSummaryHint`) ve varlık (`IdExtractor.BuildHintMessage`) sistem mesajlarını üretmek.
- **Akıllı Geçmiş Kırpma (Token Optimizasyonu):** `ConversationSummaryProvider` eski mesajları özetleyip bağlama eklediğinde, özetlenen mesajları sohbet geçmişinden akıllıca atlayarak (`SelectHistoryToSend`) mükerrer token harcamasını engellemek.
- **Hatasız Fallback Güvencesi:** Özetleyici LLM çağrısı zaman aşımına uğradığında veya özet prompt'a girmediğinde geçmişin kırpılmasını önleyerek mesaj kaybını engellemek.

## Sorumlulukları

- **Üstlendiği:**
  - `BuildWorkflowMessagesAsync` ile tüm sistem, bağlam ve kullanıcı mesajlarını sıraya dizmek.
  - Özet (summary) bağlama girmişse geçmişi kırpmak (`SelectHistoryToSend`), girmemişse tam geçmişi göndermek.
  - Doğrulanmış müşteri kimliği ipucunu (`CustomerIdentityHintBuilder`) eklemek.
  - Yönetici replan notunu tüketip sisteme eklemek (`ConsumeForceReplanHint`).

## Constructor ve Başlatma Mantığı

```csharp
public WorkflowMessageBuilder(
    IContextPipeline contextPipeline,
    IPromptRepository prompts,
    IChatClient chatClient,
    ILoggerFactory loggerFactory,
    CustomerIdentityHintBuilder identityHint)
```

### Constructor İçerisinde Yapılan İşler:
- Bağlam hattı (`_contextPipeline`), prompt deposu (`_prompts`), LLM istemcisi (`_chatClient`), log fabrikası (`_loggerFactory`) ve müşteri kimlik ipucu üreticisi (`_identityHint`) alanları başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `BuildWorkflowMessagesAsync`
```csharp
public async Task<WorkflowPrompt> BuildWorkflowMessagesAsync(
    string query,
    List<ConversationMessage>? conversationHistory,
    AgentSession? session,
    ReasoningResult? reasoning,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** İş akışına beslenecek tüm mesaj listesini ve bağlam nesnesini oluşturur.
- **İç Mantığı (Mesaj Enjeksiyon Sırası):**
  1. `_identityHint.BuildAsync`: Doğrulanmış müşteri kimliği sistem mesajı olarak eklenir.
  2. `_contextPipeline.BuildContextAsync`: Oturum hakkındaki RAG/episodik bağlam sistem mesajı olarak eklenir.
  3. `BuildReasoningSummaryHint`: Reasoning adımında üretilen niyet ve alt görevler sistem mesajı olarak eklenir.
  4. `IdExtractor.BuildHintMessage`: Sorgudan veya reasoning'den çıkarılan sipariş/ürün ID'leri sistem mesajı olarak eklenir.
  5. `SelectHistoryToSend`: Sohbet geçmişi kırpılarak eklenir.
  6. `ConsumeForceReplanHint`: Varsa yönetici müdahale notu sistem mesajı olarak eklenir.
  7. Son olarak kullanıcının anlık sorusu (`query`) `ChatRole.User` rolüyle eklenir.
  8. `WorkflowPrompt(messages, contextResult)` döndürülür.

### 2. `SelectHistoryToSend` (Private)
```csharp
private IEnumerable<ConversationMessage> SelectHistoryToSend(
    List<ConversationMessage>? conversationHistory,
    AgentSession? session,
    ContextResult contextResult)
```
- **Ne işe yarar?:** Özetlenmiş mesajların tekrar gönderilmesini önler.
- **İç Mantığı:** Eğer özet bu turda gerçekten bağlama eklenmişse, `SessionState.SummarizedMessageCount` kadar eski mesaj atlanır (`.Skip(count)`), yalnızca güncel mesajlar iletilir. Özet başarısız olmuşsa hiçbir mesaj atlanmaz.

### 3. `BuildReasoningSummaryHint` (Private)
- **Ne işe yarar?:** `ReasoningResult` içerisindeki `Intent`, `Confidence`, `MissingContext` ve `SubTasks` bilgilerini uzman ajanların anlayacağı kısa bir sistem ipucu metnine dönüştürür.

## Bağımlılıklar

- `Microsoft.Extensions.AI.ChatMessage`
- `CustomerSupportBot.Application.Services.Providers.CustomerIdentityHintBuilder`
- `CustomerSupportBot.Application.Ports.Outbound.IPromptRepository`
- [ReasoningResult](../CustomerSupportBot.Domain/Model/ReasoningResult.md)
- [AgentSession](../CustomerSupportBot.Domain/Model/AgentSession.md)
