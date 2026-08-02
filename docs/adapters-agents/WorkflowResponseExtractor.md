# WorkflowResponseExtractor

**Dosya:** `CustomerSupportBot.Adapters.Agents/WorkflowResponseExtractor.cs`  
**Tür:** `public static class`

## Ne yapar?

MAF workflow'u tamamlandığında `WorkflowOutputEvent` üretir. Bu event'in içindeki `Data` alanı birden fazla formatta gelebilir (`IEnumerable<ChatMessage>`, tek `ChatMessage` veya string). `WorkflowResponseExtractor`, bu ham veriden:

1. **Son kullanıcı yanıtını** çıkarır
2. **Teknik iç verileri** (TERMINATE marker, JSON blokları, agent adları) temizler
3. **Planning ve Specialist reasoning** verilerini trace için ayrıştırır
4. **SSE streaming** için metni küçük parçalara böler

## Metodlar

### `ExtractResultFromOutput`

```csharp
public static string ExtractResultFromOutput(WorkflowOutputEvent output)
```

`WorkflowOutputEvent.Data`'yı üç farklı formatta işler:

| Data türü | Davranış |
|-----------|---------|
| `IEnumerable<ChatMessage>` | TERMINATE içeren son assistant mesajını bul; yoksa routing içermeyen son assistant mesajını al |
| `ChatMessage` | Doğrudan `.Text` |
| `string` | Doğrudan döner |
| Diğer | Boş string |

**TERMINATE önceliği:** Birden fazla assistant mesajı varsa, `TERMINATE` marker'ı içeren son mesaj tercih edilir çünkü bu ResponseAgent'ın bilinçli sonlandırma mesajıdır.

### `ExtractPlanningFromOutput`

```csharp
public static PlanningResult? ExtractPlanningFromOutput(WorkflowOutputEvent output)
```

Konuşma geçmişinden PlanningAgent'ın mesajını bulur ve `PlanningResultParser.TryParse` ile parse eder. Sonuç `ReasoningTrace.Planning` alanına yazılır.

**PlanningAgent mesajını nasıl bulur?** İki koşuldan biri sağlanırsa mesaj "planning mesajı" kabul edilir:
1. `AuthorName == "PlanningAgent"` veya
2. Metin `"selected_agent"` JSON property'sini içeriyor

### `ExtractSpecialistReasoningsFromOutput`

```csharp
public static List<SpecialistReasoning> ExtractSpecialistReasoningsFromOutput(WorkflowOutputEvent output)
```

Geçmişteki tüm specialist ajan mesajlarını tarar. Bir mesaj aşağıdaki JSON property'lerden birini içeriyorsa "specialist reasoning" olarak kabul edilir:
- `"preToolCheck"`
- `"resultConfidence"`
- `"postToolReflection"`

Bulunan mesajlar `SpecialistReasoningParser.TryParse` ile parse edilir. Trace'deki `SpecialistReasonings` listesine eklenir.

### `RemoveTerminationMarkers`

```csharp
public static string RemoveTerminationMarkers(string result)
```

Kullanıcıya gönderilecek metinden `TERMINATE` ve sonrasını siler.

**Regex:**
```
TERMINATE(\s*[:\s]+reason\s*=\s*[a-zA-Z_]+|\s*\([^)]+\))?[\s\S]*$
```

Tüm regex çağrıları 500ms `matchTimeout` (`RegexTimeout` sabiti) ile çalışır; `RegexMatchTimeoutException` yakalanırsa temizlenmemiş orijinal metin döner (crash yerine best-effort davranış). LLM çıktısı (potansiyel prompt-injection kaynaklı adversarial metin) üzerinde çalıştığı için bu, ReDoS'a karşı savunma katmanıdır.

**Örnekler:**

| Giriş | Çıkış |
|-------|-------|
| `"Siparişiniz oluşturuldu. TERMINATE"` | `"Siparişiniz oluşturuldu."` |
| `"Yanıt hazır. TERMINATE reason=completed"` | `"Yanıt hazır."` |
| `"Tamam. TERMINATE(escalated)"` | `"Tamam."` |

### `RemoveTechnicalJsonBlocks`

```csharp
public static string RemoveTechnicalJsonBlocks(string result)
```

Specialist ajanların ürettiği iç JSON bloklarını metinden siler. Bu bloklar müşteri arayüzüne sızmamalıdır.

**Silinen JSON anahtarları:** `preToolCheck`, `resultConfidence`, `postToolReflection`, `selfCritique`

**İki aşamalı temizlik:**
1. Kod bloğu içindeki JSON'lar silinir (` ```json { ... } ``` ` formatı)
2. Düz JSON nesneleri silinir (`{ ... }` formatı)
3. Üç veya daha fazla ardışık boş satır `\n\n`'e indirgenir

> **Bilinen sınırlama:** Adım 2'deki brace-eşleştirme regex'i yalnızca **tek seviye** iç içe geçmeyi destekler (`(?:[^{}]|(?:\{[^{}]*\}))*`) — 2+ seviye derin iç içe JSON bloklarını kaçırabilir. Kapsamlı bir brace-counting parser'a geçiş yapılmadı; bunun yerine timeout eklendi (aşağıya bakın).

### `ContainsAgentRoutingMessage`

```csharp
public static bool ContainsAgentRoutingMessage(string text)
```

Metnin içinde herhangi bir ajan adı (`WellKnown.AgentNames.All` listesi) geçiyorsa `true` döner. Bu durumda `CustomerSupportTeam.RewriteRoutingMessageAsync` çağrılır.

**Neden gerekli?** PlanningAgent bazen kime yönlendirdiğini açıkça yazabilir: `"OrderAgent: sipariş bilgisi için yönlendiriyorum"`. Bu teknik mesaj kullanıcıya gösterilmemeli; LLM ile kullanıcı dostu hale getirilmeli.

### `ParseTerminationReasonFromResult`

```csharp
public static string? ParseTerminationReasonFromResult(string text, ILogger? logger = null)
```

TERMINATE marker'ının yanındaki nedeni çıkarır ve `WellKnown.Termination.KnownReasons` kümesiyle doğrular.

| Metin | Sonuç |
|-------|-------|
| `"... TERMINATE reason=escalation_needed"` | `"escalation_needed"` |
| `"... TERMINATE(awaiting_user_input)"` | `"awaiting_user_input"` |
| `"... TERMINATE reason=Completed"` | `"completed"` (case-insensitive normalize) |
| `"... TERMINATE reason=bilinmeyen_deger"` | `null` + warning log (çağıran `completed` fallback'i uygular) |
| `"... TERMINATE"` | `null` |

Reason uzayı iki kategoriden oluşur (`WellKnown.Termination` altında sabitler):

- **LLM (ResponseAgent) ürettiği:** `completed`, `awaiting_user_input`, `escalation_needed`, `not_found`, `error` — response-agent.md prompt'undaki listeyle birebir hizalıdır.
- **Sistem (guard) ürettiği:** `max_messages_reached`, `repeated_tool_call_guard`, `timeout` — LLM bunları üretmez; guard/timeout yolları `terminationReason`'u doğrudan ayarlar.

Bu değer trace'e ve `ResponseStart`/`ResponseComplete` event'lerine eklenir.

### `ExtractDeltaText`

```csharp
public static string ExtractDeltaText(object? data)
```

`ResponseDelta` stream event'inin `data` nesnesinden `text` property'sini reflection ile okur. Compound query streaming'de alt görev metinlerini toplamak için kullanılır.

### `IsInternalWorkflowExecutor`

```csharp
public static bool IsInternalWorkflowExecutor(string executorId)
```

MAF'ın iç orkestrasyon executorlarını (gerçek ajan olmayan) filtreler. `WellKnown.SystemExecutorPrefixes` listesindeki bir prefix ile başlayan executor ID'leri "iç executor" kabul edilir ve `StreamEvent` olarak yayımlanmaz.

### `StreamTextInChunksAsync`

```csharp
public static async IAsyncEnumerable<string> StreamTextInChunksAsync(
    string text, CancellationToken ct)
```

Tam metni boşluk ve satır sonu karakterlerinde bölerek kelime kelime (veya satır satır) akışa gönderir. Her parça arasında 20ms beklenir — bu gecikme frontend'de "yazıyor" efekti sağlar.

**Örnek:**

```
"Merhaba Ahmet Bey!" → ["Merhaba ", "Ahmet ", "Bey!"]
                         ↑20ms↑    ↑20ms↑
```

## Ne zaman bu dosyaya dokunursunuz?

- MAF yeni bir output event formatı üretiyorsa → `ExtractResultFromOutput` güncelleyin
- Yeni bir "teknik JSON key" eklendiyse → `RemoveTechnicalJsonBlocks` içindeki `technicalKeys` sabitini güncelleyin
- Yeni bir ajan adı eklendiyse → `ContainsAgentRoutingMessage` `WellKnown.AgentNames.All`'ı kullandığından otomatik çalışır; `WellKnown`'ı güncellemek yeterli
- Streaming hızını değiştirmek istiyorsanız → `StreamTextInChunksAsync` içindeki `Task.Delay(20, ct)` değerini ayarlayın
