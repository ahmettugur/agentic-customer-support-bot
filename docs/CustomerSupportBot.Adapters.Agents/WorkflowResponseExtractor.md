# WorkflowResponseExtractor

**Dosya:** `CustomerSupportBot.Adapters.Agents/WorkflowResponseExtractor.cs`  
**Tür:** `public static class`

## Ne yapar?

MAF workflow'u tamamlandığında `WorkflowOutputEvent` üretir. Bu event'in içindeki `Data` alanı birden fazla formatta gelebilir (`IEnumerable<ChatMessage>`, tek `ChatMessage` veya string). `WorkflowResponseExtractor`, bu ham veriden:

> 💡 **Analiz notu:** Workflow sonucunu "ham madde"den "mamul"e çeviren fabrika gibi — TERMINATE marker'ları, JSON blokları, agent isimleri gibi teknik detayları temizler ve müşteriye gösterilecek temiz metin üretir.

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
| ----------- | --------- |
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
| ------- | ------- |
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
| ------- | ------- |
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

GroupChat topolojisinde ölçülen (MAF 1.17.0) gerçek durum: filtrelenen **tek** id `GroupChatHost`, geçen 6 id ise `{AjanAdı}_{guid}` biçimindeki ajanlar. Eşleşme `StartsWith` olduğu için guid soneki sorun çıkarmaz. Listenin neden bu kadar kısa olduğu ve topoloji değişirse ne olacağı için bkz. [WellKnown.md → SystemExecutorPrefixes](../CustomerSupportBot.Domain/WellKnown.md#systemexecutorprefixes).

### `SplitIntoDeltaChunks`

```csharp
public static IEnumerable<string> SplitIntoDeltaChunks(string text)
```

Tamamlanmış bir metni `response_delta` olaylarına bölmek için boşluk/satır sonu sınırlarında parçalar. **Kayıpsızdır** — parçaların birleşimi girdiye birebir eşittir.

**Örnek:**

```
"Merhaba Ahmet Bey!" → ["Merhaba ", "Ahmet ", "Bey!"]
```

#### Parçalama neden gerekli — delta'lar kozmetik değil

`response_complete`'in **sunucu tarafında hiçbir tüketicisi yoktur**; yalnızca tarayıcıya gider. Buna karşılık:

| Tüketici | Ne yapar |
|---|---|
| `ChatPortService` | Delta'ları birleştirip `PersistExchangeAsync` ile **konuşma geçmişine yazar** |
| `RealtimeBridgeService` | Delta'ları birleştirip `SpeakTextAsync` ile **TTS'e okutur** |
| `DecomposedRunner` | Alt görev delta'larını birleştirip alt görev yanıtını kurar |

Yani delta akışı kalıcılığın ve sesin kaynağıdır. Bu yüzden kayıpsızlık bir "temizlik" isteği değil, doğruluk şartıdır: bir karakter kaybolsa kullanıcıya doğru metin görünür (`response_complete` baloncuğun üzerine yazar) ama **DB'ye ve sese bozuk metin gider** — hata sessiz kalırdı. `SplitIntoDeltaChunks_IsLossless` bunu kilitler.

> 🐞 **Kaldırıldı: kelime başına 20 ms yapay gecikme.** Metot eskiden `StreamTextInChunksAsync` adıyla her parçadan sonra `await Task.Delay(20, ct)` yapıyordu — yalnızca "yazıyor" hissi vermek için, hiçbir teknik gerekçesi olmadan. Ölçüm: **~20.8 ms/kelime** → 200 kelimede **4.2 sn**, 400 kelimede **8.3 sn**, üstelik yanıt zaten hesaplanıp DB'ye yazıldıktan *sonra*. İki somut zararı vardı:
>
> 1. **Yarım kalan yanıt.** `WorkflowRunner` bu metoda turun timeout'una bağlı token'ı veriyordu. Workflow bütçenin sonuna yakın normal bitip yapay akış timeout'u aşarsa `Task.Delay` `OperationCanceledException` fırlatıyor, bu iterator'dan dışarı sızıyor ve `response_complete` **hiç gönderilmiyordu** — cevap DB'de tam, kullanıcının ekranında yarım.
> 2. **Gereksiz kaynak tutma.** MAF `StreamingRun` nesnesi ve SSE bağlantısı, yanıt hazır olduktan sonra saniyelerce açık kalıyordu.
>
> Gecikme gidince iptal edilecek bekleme noktası da kalmadı: metot artık **senkron**, `CancellationToken` almıyor ve hiçbir koşulda fırlatmıyor — `response_complete` teslimi yapısal olarak garanti. Kelime kelime bölme sürüyor (maliyeti yok; yavaş bağlantıda tarayıcı ilk kelimeleri erken çizebiliyor). `SplitIntoDeltaChunks_LongText_CompletesImmediately` gecikmenin geri gelmesini engeller.

> ℹ️ Bu, **gerçek** token akışının yerine geçmez. Mümkün olan yerde gerçek akış (`AgentResponseUpdateEvent`) tercih edilir; burası yalnızca elde tamamlanmış bir metin olduğunda kullanılır — bkz. [ContextProviders / WorkflowRunner](WorkflowRunner.md).

## Ne zaman bu dosyaya dokunursunuz?

- MAF yeni bir output event formatı üretiyorsa → `ExtractResultFromOutput` güncelleyin
- Yeni bir "teknik JSON key" eklendiyse → `RemoveTechnicalJsonBlocks` içindeki `technicalKeys` sabitini güncelleyin
- Yeni bir ajan adı eklendiyse → `ContainsAgentRoutingMessage` `WellKnown.AgentNames.All`'ı kullandığından otomatik çalışır; `WellKnown`'ı güncellemek yeterli
- Yapay "yazıyor" gecikmesi **bilerek kaldırıldı** (yukarıdaki 🐞 notu) — geri eklemeyin; kullanıcıya daha hızlı akış isteniyorsa çözüm gerçek token akışını (`AgentResponseUpdateEvent`) daha çok yolda kullanmaktır, sahte gecikmeyi ayarlamak değil
