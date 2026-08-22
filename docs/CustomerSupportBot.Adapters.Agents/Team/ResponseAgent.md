# ResponseAgent

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/ResponseAgent.cs`
- **Tür:** `internal sealed class : SupportAgentBase`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`ResponseAgent`, çoklu ajan grup sohbetinin son adımında devreye giren; önceki uzman ajanların araç çıktılarını ve ReAct akıl yürütme JSON'larını kullanıcıya sunulacak nihai, nazik ve tutarlı bir Türkçe yanıta dönüştüren ve sonuna `TERMINATE: reason=...` belirtecini ekleyerek MAF iş akışını sonlandıran sentezleyici ajandır. Hiçbir aracı (tool) yoktur.

## Hangi amaçla kullanılır`?

- Uzmanların teknik JSON çıktılarını ve ara durumlarını son kullanıcıya göstermemek.
- Kullanıcıya canlı olarak akan SSE token akışını üretmek (Kullanıcının gördüğü gerçek zamanlı yanıt bu ajandan akar).
- Cevabın sonuna `TERMINATE` ekleyerek iş akışının başarıyla durmasını sağlamak.

## Sorumlulukları

- **Üstlendiği:**
  - `agents/response-agent` sistem istemini bağlamak.
  - Düz metin Türkçe yanıt üretmek ve sonlanma belirtecini eklemek.
  - Breakpoint noktalarında (`OnBeforeRun`, `OnAfterRun`) uzmanların ham çıktılarını ve üretilen nihai metni yakalamak.

## Constructor ve Başlatma Mantığı

```csharp
public ResponseAgent(IChatClient chatClient, IPromptRepository prompts)
    : base(BuildInner(chatClient, prompts))
```

### Constructor İçerisinde Yapılan İşler:
- `BuildInner` statik metodunu çağırarak `ChatClientAgent` nesnesini yapılandırır ve `SupportAgentBase` temel sınıfına devreder.

## Metotlar ve İç Çalışma Mantıkları

### 1. `BuildInner` (Private Static)
```csharp
private static ChatClientAgent BuildInner(IChatClient chatClient, IPromptRepository prompts)
```
- **Ne işe yarar?:** Yanıt sentezleyici ajan için MAF `ChatClientAgent` örneğini kurar.
- **İç Mantığı:**
  1. `Name`: `WellKnown.AgentNames.Response` ("ResponseAgent") atanır.
  2. `Instructions`: `prompts.Get("agents/response-agent")` ile yüklenir.
  3. Düz metin yanıt ürettiği için özel bir `ResponseFormat` atanmaz.

### 2. `OnBeforeRun` (Protected Override)
```csharp
protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
```
- **Ne işe yarar?:** LLM'e giden tam mesaj listesini (uzman ajanların ReAct JSON'ları ve araç sonuçları) inceler (Breakpoint noktası).

### 3. `OnAfterRun` (Protected Override)
```csharp
protected override void OnAfterRun(AgentResponse response)
```
- **Ne işe yarar?:** Üretilen nihai yanıt metnini ve sonundaki `TERMINATE` işaretçisini yakalar (Breakpoint noktası).

## Bağımlılıklar

- [SupportAgentBase](SupportAgentBase.md)
- `CustomerSupportBot.Application.Ports.Outbound.IPromptRepository`
- `Microsoft.Extensions.AI.IChatClient`
