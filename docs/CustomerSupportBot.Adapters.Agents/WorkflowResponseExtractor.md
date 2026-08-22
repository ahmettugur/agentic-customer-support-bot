# WorkflowResponseExtractor

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/WorkflowResponseExtractor.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`WorkflowResponseExtractor`, Microsoft Agents Framework (MAF) iş akışı çıktı olaylarından (`WorkflowOutputEvent`) ve ara mesaj koleksiyonlarından (`IEnumerable<ChatMessage>`) anlamlı sonuç metinlerini, planlama modellerini (`PlanningResult`), uzman ReAct JSON'larını (`SpecialistReasoning`) ve sonlanma/yönlendirme sinyallerini çıkaran yardımcı sınıftır.

## Hangi amaçla kullanılır`?

Uzman ajanların ürettiği `SpecialistReasoningSchema` JSON çıktılarını ve `ResponseAgent`'ın `TERMINATE` işaretçilerini ayrıştırmak, ReDoS saldırılarına karşı korumalı regex zaman aşımı (`RegexTimeout = 500ms`) kullanmak ve anormal sonlanma durumlarında bile ara durumlardan plan/eskalasyon verisi kurtarmak için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `ExtractResultFromOutput`/`ExtractPlanningFromOutput`/`ExtractSpecialistReasoningsFromOutput` ile nihai `WorkflowOutputEvent`'ten sonuç/plan/uzman verisi çıkarmak.
  - `ExtractPlanning`/`ExtractSpecialistReasonings` ile aynı ayrıştırmayı ARA (henüz tamamlanmamış, ör. `ExecutorCompletedEvent.Data`) mesaj koleksiyonları üzerinde de yapabilmek — timeout/hata durumunda bile plan/eskalasyon verisi kurtarılabilsin diye.
  - Sonuç metnini kullanıcıya göstermeden önce temizlemek: `RemoveTerminationMarkers`, `RemoveTechnicalJsonBlocks`, `ParseTerminationReasonFromResult`.
  - Tamamlanmış bir metni kayıpsız biçimde `response_delta` parçalarına bölmek: `SplitIntoDeltaChunks`.
  - Tüm regex işlemlerinde ortak bir zaman aşımı (`RegexTimeout = 500ms`) uygulayarak LLM çıktısı üzerinde çalışan desenleri ReDoS'a karşı korumak.

## Metotlar / Üyeler

| Üye | İmza | Açıklama |
|---|---|---|
| `ExtractResultFromOutput` | `public static string ExtractResultFromOutput(WorkflowOutputEvent output)` | Önce `TERMINATE` işaretli son asistan mesajını arar; yoksa ajan-yönlendirme metni İÇERMEYEN son asistan mesajını, o da yoksa herhangi bir son asistan mesajını döner. `output.Data` tek bir `ChatMessage` veya düz `string` ise onu doğrudan döner. |
| `ExtractPlanningFromOutput` / `ExtractPlanning` | `public static PlanningResult? ExtractPlanningFromOutput(WorkflowOutputEvent output)` / `ExtractPlanning(IEnumerable<ChatMessage>)` | `PlanningAgent`'ın (veya JSON içeriğinden `selectedAgent` alanı geçen) mesajını bulup `PlanningResultParser.TryParse` ile çözer. |
| `ExtractSpecialistReasoningsFromOutput` / `ExtractSpecialistReasonings` | `public static List<SpecialistReasoning> ExtractSpecialistReasonings(IEnumerable<ChatMessage> chatMessages)` | Yazarı bir uzman ön ekiyle başlayan VE `preToolCheck`/`resultConfidence`/`postToolReflection` anahtarlarından birini içeren mesajları `SpecialistReasoningParser.TryParse` ile ayrıştırır. |
| `ContainsHumanHandoffToolCall` | `public static bool ContainsHumanHandoffToolCall(IEnumerable<ChatMessage> chatMessages)` | `human_handoff_tool`'un gerçekten çağrılıp çağrılmadığını `FunctionCallContent` üzerinden deterministik olarak tespit eder — LLM'in `postToolReflection.status=needs_escalation` JSON'unu doğru üretmesine güvenmez, `WorkflowRunner.EnsureHumanHandoffEscalation`'ın ek güvencesidir. |
| `RemoveTerminationMarkers` | `public static string RemoveTerminationMarkers(string result)` | `TERMINATE[...]` işaretini ve sonrasındaki her şeyi metinden siler. |
| `RemoveTechnicalJsonBlocks` | `public static string RemoveTechnicalJsonBlocks(string result)` | Yanıt metnine sızmış teknik JSON bloklarını (kod bloğu içinde veya çıplak, `preToolCheck`/`resultConfidence`/`postToolReflection`/`selfCritique` anahtarlarını içeren) siler. Yalnızca tek seviye iç içe brace eşleştirir — bilinen sınırlama, best-effort temizliktir. |
| `ContainsAgentRoutingMessage` | `public static bool ContainsAgentRoutingMessage(string text)` | Metin `WellKnown.AgentNames.All`'daki herhangi bir ajan adını içeriyor mu (yönlendirme mesajı sızıntısı tespiti). |
| `ParseTerminationReasonFromResult` | `public static string? ParseTerminationReasonFromResult(string text, ILogger? logger = null)` | `TERMINATE: reason=xxx` veya `TERMINATE(xxx)` kalıbından reason'ı çıkarıp `WellKnown.Termination.KnownReasons` ile doğrular; bilinmeyen/olmayan reason için `null` döner (çağıran taraf `ReasonCompleted`'a düşer). |
| `ExtractDeltaText` | `public static string ExtractDeltaText(object? data)` | `TextDeltaPayload`'dan ham metni çıkarır. |
| `IsInternalWorkflowExecutor` | `public static bool IsInternalWorkflowExecutor(string executorId)` | `executorId`'nin MAF'ın dahili sistem executor önekleriyle (`WellKnown.SystemExecutorPrefixes`) başlayıp başlamadığını kontrol eder — trace'te gösterilmemesi gereken iç düğümleri filtrelemek için. |
| `SplitIntoDeltaChunks` | `public static IEnumerable<string> SplitIntoDeltaChunks(string text)` | Tamamlanmış bir metni kelime sınırlarında (boşluk/yeni satırdan sonra) kayıpsız parçalara böler — parçaların birleşimi girdiye birebir eşittir. |

> 🐞 **`SplitIntoDeltaChunks`'tan kaldırılan yapay gecikme:** Eskiden her parça arasında amaçsız `Task.Delay(20ms)` vardı ("yazıyor" hissi için). 200 kelimelik bir yanıtta ~4.2sn, 400 kelimede ~8.3sn gecikme ekliyordu — hem de yanıt zaten hesaplanıp DB'ye yazıldıktan SONRA. İki somut zararı vardı: (1) `WorkflowRunner`'ın turn timeout'u bu bekleme sırasında dolabiliyordu, bu da `response_complete`'in HİÇ gönderilmemesine yol açıyordu (cevap DB'de tam, ekranda yarım); (2) MAF `StreamingRun`/SSE bağlantısı gereksiz yere saniyelerce açık kalıyordu. Gecikme kaldırıldı, metot artık senkron ve hiçbir koşulda fırlatmıyor — `response_complete`'in gönderilmesi yapısal olarak garanti.

## Bağımlılıklar

- `Microsoft.Agents.AI.Workflows.WorkflowOutputEvent`
- [PlanningResult](../CustomerSupportBot.Domain/Model/PlanningResult.md)
- [SpecialistReasoning](../CustomerSupportBot.Domain/Model/SpecialistReasoning.md)
- `System.Text.RegularExpressions.Regex` (500ms timeout ile)
