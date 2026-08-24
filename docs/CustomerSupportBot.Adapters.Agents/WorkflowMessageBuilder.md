# WorkflowMessageBuilder

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/WorkflowMessageBuilder.cs`
- **Tür:** `internal sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`WorkflowMessageBuilder`, Microsoft Agents Framework (MAF) grup sohbeti iş akışı başlatılmadan önce; context pipeline (RAG + episodik bellek), müşteri kimliği ipuçları, Reasoning özetleri, yönetici replan notları ve geçmiş konuşma mesajlarını sıralı ve optimize bir şekilde derleyerek `WorkflowPrompt` (mesajlar + bağlam durumu) üreten derleyicidir.

> 🐞 **Geçmişte farklıydı:** Bu sınıf eskiden ayrıca deterministik bir "entity hint" (order_id/
> customer_id/complaint_id'nin `IdExtractor` ile metinden regex ile çıkarılıp "order_id MEVCUT:
> 1041" gibi bir sistem mesajı olarak enjekte edilmesi) üretiyordu. `IdExtractor` tamamen
> kaldırıldı — order_id/complaint_id çözümü artık tamamen LLM'e bırakılıyor: specialist
> agent'lar kullanıcı mesajını doğrudan okuyup ilgili tool'a parametre olarak geçiriyor; hiç
> geçmezse `get_last_order_tool` gibi parametresiz tool'lar devreye giriyor (bkz.
> `planning-agent.md`).

## Hangi amaçla kullanılır`?

- **Sistem İpuçlarının Doğru Sırada Enjeksiyonu:** Ajan prompt'larının beklediği formatta kimlik (`CustomerIdentityHint`), bağlam (`ContextResult`) ve muhakeme (`ReasoningSummaryHint`) sistem mesajlarını üretmek.
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
  4. `SelectHistoryToSend`: Sohbet geçmişi kırpılarak eklenir.
  5. `ConsumeForceReplanHint`: Varsa yönetici müdahale notu sistem mesajı olarak eklenir.
  6. Son olarak kullanıcının anlık sorusu (`query`) `ChatRole.User` rolüyle eklenir.
  7. `WorkflowPrompt(messages, contextResult)` döndürülür.

### 2. `SelectHistoryToSend` (Internal Static)
```csharp
internal static IEnumerable<ConversationMessage> SelectHistoryToSend(
    List<ConversationMessage>? conversationHistory,
    AgentSession? session,
    ContextResult contextResult)
```
- **Ne işe yarar?:** Özetlenmiş mesajların tekrar gönderilmesini önler. `internal static`'tir — dış bağımlılık gerektirmediği için birim testlerinde doğrudan çağrılabilir.
- **İç Mantığı (savunmacı sırayla):**
  1. Geçmiş boşsa `[]` döner.
  2. `SessionState.SummarizedMessageCount` `0` ise ya da `ConversationSummary` boşsa — özet hiç üretilmemiş demektir, hiçbir mesaj atlanmaz (tam geçmiş döner).
  3. Özet bu turda **gerçekten** `ContextResult`'a girmediyse (`contextResult.Included(ConversationSummaryProvider.ProviderName)` `false`) — özetleyici LLM çağrısı hata verdi/zaman aşımına uğradı demektir; geçmiş yine kırpılmaz. Bu kontrol olmadan (yalnızca `SessionState`'e bakılsaydı) o turlar modelin görüş alanından tamamen kaybolurdu, çünkü `SessionState.ConversationSummary` provider `null` dönse bile eski değerini korur.
  4. `summarized >= conversationHistory.Count` ise (state geçmişten büyükse, ör. geçmiş temizlenmiş ama state kalmışsa) yine kırpılmaz — özetlenmemiş bir mesajı yanlışlıkla düşürmektense fazladan mesaj göndermek tercih edilir.
  5. Aksi halde `conversationHistory.Skip(summarized)` — özetin kapsadığı baştaki turlar atlanır.

### 3. `ConsumeForceReplanHint` (Private Static)
```csharp
private static string? ConsumeForceReplanHint(AgentSession? session)
```
- **Ne işe yarar?:** Admin panelinden tetiklenen `ForceReplanNextTurn` bayrağını okuyup **tek kullanımlık** olarak temizler.
- **İç Mantığı:** `lock (session)` içinde bayrak kontrol edilir; `true` ise `WellKnown.FallbackMessages.ReplanPlanningHint` (+ varsa admin'in `ReplanNote`'u) döndürülür ve bayrak+not hemen sıfırlanır. Kilit, paralel alt görevlerin veya eşzamanlı isteklerin aynı bayrağı birden fazla kez tüketmesini engeller.

### 4. `BuildReasoningSummaryHint` (Internal)
```csharp
internal string BuildReasoningSummaryHint(ReasoningResult r)
```
- **Ne işe yarar?:** `ReasoningResult` içerisindeki `Analysis`, `Intent`, `Steps` ve `RequiredInfo`, `NextAction` alanlarını uzman ajanların anlayacağı kısa bir sistem ipucu metnine dönüştürür (`services/reasoning-hint` prompt şablonu üzerinden render edilir).
- **Not:** Burada eskiden birleşik (compound) sorgular için PlanningAgent'a "alt görevleri sırayla aynı yanıtta yönlendir" diyen bir blok vardı; kaldırıldı çünkü `DecomposedRunner`'ın gerçek çalışma şeklini (her alt görev AYRI bir `_runner.RunAsync` çağrısıyla koşar) yanlış tarif ediyordu.

### 5. `RewriteRoutingMessageAsync`
```csharp
public async Task<string> RewriteRoutingMessageAsync(
    string routingMessage, string originalQuery, CancellationToken ct)
```
- **Ne işe yarar?:** Bir uzman ajanın iç yönlendirme/handoff mesajını (teknik, İngilizce olabilen) müşteriye gösterilecek doğal bir Türkçe cümleye LLM ile yeniden yazar.
- **İç Mantığı:** `services/routing-rewrite-system`/`-user` prompt şablonlarıyla `_chatClient.GetResponseAsync` çağrılır. Hata olursa (zaman aşımı, LLM hatası) sessizce yutulmaz — `LogWarning` ile loglanır ve `WellKnown.FallbackMessages.RoutingRewrite` sabit mesajı döner; bu, sürekli patlayan bir çağrının görünmez kalmasını önler.

### 6. `ToChatRole` (Private Static)
```csharp
private static ChatRole ToChatRole(string role)
```
- **Ne işe yarar?:** Uygulama içi `ConversationRoles` string sabitlerini (`User`, `System`, diğer her şey `Assistant` sayılır) MAF'ın `ChatRole` tipine çevirir.

## `WorkflowPrompt` (record)

```csharp
internal sealed record WorkflowPrompt(List<ChatMessage> Messages, ContextResult Context);
```

`BuildWorkflowMessagesAsync`'in dönüş tipi. `Context` alanı yalnızca gözlemlenebilirlik için değildir — çağıran taraf (`WorkflowRunner`), özetin bu turda gerçekten prompt'a girip girmediğine göre geçmişi kırpma kararı verir (bkz. `SelectHistoryToSend`).

## Bağımlılıklar

- `Microsoft.Extensions.AI.ChatMessage` / `IChatClient`
- `CustomerSupportBot.Application.Services.Providers.CustomerIdentityHintBuilder`
- `CustomerSupportBot.Application.Ports.Outbound.IPromptRepository`
- [ReasoningResult](../CustomerSupportBot.Domain/Model/ReasoningResult.md)
- [AgentSession](../CustomerSupportBot.Domain/Model/AgentSession.md)
