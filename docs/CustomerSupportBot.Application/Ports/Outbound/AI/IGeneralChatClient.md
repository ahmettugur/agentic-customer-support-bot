# IGeneralChatClient

**Kaynak:** `Ports/Outbound/AI/IGeneralChatClient.cs`
**Implementasyon:** [`GeneralChatClientAdapter`](../../../../CustomerSupportBot.Adapters.AI/Chat/GeneralChatClientAdapter.md)

## 1. Ne İşe Yarar

Genel amaçlı (reasoning olmayan) LLM metin tamamlama için tek metot içeren minimal port:
mesaj listesi verilir, tam metin cevabı alınır.

## 2. Hangi Amaçla Kullanılır

Reasoning modeli kadar güçlü/pahalı olmasına gerek olmayan iç işler için kullanılır — örn.
`ConversationSummaryProvider`'ın konuşma özetini üretmesi.

## 3. Sorumlulukları

- **Üstlendiği:** `ConversationMessage` listesini alıp tek bir `string` cevap döndürmek.
- **Üstlenmediği:** Streaming (bkz. [`IReasoningChatClient.StreamAsync`](IReasoningChatClient.md)
  reasoning tarafında var, bu port'ta yok), framework'e özgü tip sızıntısı (MAF'ın
  `IChatClient`/`ChatMessage` tipleri bu port'un arkasında kalır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.AI/Chat/GeneralChatClientAdapter` implemente eder; içeride MAF'ın `IChatClient`'ını
sarar ve `ConversationMessage` ↔ `ChatMessage` dönüşümünü yapar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Application katmanı Microsoft.Extensions.AI'nin somut tiplerine bağımlı olmasın diye bu ince
port var — DIP (Dependency Inversion Principle). Ayrı bir port olmasının nedeni
`IReasoningChatClient`'tan farklı bir model/ayar kullanabilmesi (daha ucuz/hızlı bir model).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<string> CompleteAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken ct = default)` | Verilen mesaj listesiyle LLM'den tam metin yanıtı alır. |

## 7. Bağımlılıklar

Port arayüzü yalnızca `CustomerSupportBot.Domain.Model.ConversationMessage`'a bağımlıdır.
