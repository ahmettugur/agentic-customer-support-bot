# IReasoningChatClient

**Kaynak:** `Ports/Outbound/AI/IReasoningChatClient.cs`
**Implementasyon:** [`ReasoningChatClient`](../../../../CustomerSupportBot.Adapters.AI/Chat/ReasoningChatClient.md)

## 1. Ne İşe Yarar

İki aşamalı akıl yürütme (niyet çıkarımı, planlama, self-critique) için kullanılan reasoning
modeline erişimi soyutlar. Hem tek seferlik tamamlama (`CompleteAsync`) hem streaming
(`StreamAsync`) sunar.

## 2. Hangi Amaçla Kullanılır

`ReasoningEngine`/planlama servisleri (bkz. Application/Services/Reasoning) kullanıcı sorgusunu
analiz ederken, plan üretirken ve self-critique yaparken bu port'u çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Reasoning modeliyle mesaj tamamlama, model adı/effort seviyesini raporlama.
- **Üstlenmediği:** JSON çıktısının ayrıştırılması — bu iş Domain katmanındaki
  `ReasoningResultParser`/`PlanningResultParser` gibi parser'lara aittir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.AI/Chat/ReasoningChatClient` implemente eder; MAF'ın `IChatClient`'ını sarar ve
`TelemetryChatClient` dekoratörüyle (bkz. `Adapters.Telemetry/Chat/TelemetryChatClient.md`)
zincirlenir — token/maliyet ölçümü bu zincir üzerinden geçer.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`IGeneralChatClient`'tan ayrı bir port olmasının nedeni farklı bir model/`ReasoningEffort`
kullanabilmesi — reasoning genelde daha pahalı/güçlü bir model gerektirir, genel amaçlı
tamamlamalar (özetleme gibi) daha ucuz bir modelle yapılabilir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `string ModelName { get; }` | Kullanılan modelin adı. |
| `string ReasoningEffort { get; }` | Reasoning effort seviyesi (low/medium/high). |
| `Task<string> CompleteAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken ct = default)` | Tek seferlik reasoning tamamlama — tam metni döner. |
| `IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken ct = default)` | Streaming reasoning — token parçaları döner. |

## 7. Bağımlılıklar

Port arayüzü yalnızca `CustomerSupportBot.Domain.Model.ConversationMessage`'a bağımlıdır.
