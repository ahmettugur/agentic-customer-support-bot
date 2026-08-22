# ConversationMessage

**Dosya:** `Model/ConversationMessage.cs`  
**Tür:** `sealed record` (immutable)  
**İlişkili:** `ConversationRoles` static class (aynı dosyada)

## 1. Ne İşe Yarar

Bot'un dahili konuşma geçmişi formatıdır — LLM'lere geçirilen her mesajı (system prompt, kullanıcı sorusu, asistan yanıtı) temsil eder.

## 2. Hangi Amaçla Kullanılır

`ISessionManager.GetHistoryAsync()` bu tipte bir liste döner. Reasoning ve workflow mesaj builder'ları bu listeyi kullanarak LLM'e gönderilecek prompt zincirini oluşturur.

> 💡 **Analiz notu:** `ChatBridgeMessage` ile karıştırma!
>
> - `ConversationMessage` = **LLM context** için ham geçmiş (sistem prompt'u, user query, assistant response)
> - `ChatBridgeMessage` = **canlı sohbet UI** mesajları (admin panelindeki chat)

## 3. Sorumlulukları

- ✅ Tek bir konuşma mesajını (rol + metin) taşımak
- ✅ LLM API'sine gönderilen mesaj formatını standartlaştırmak
- ❌ Mesajı kaydetmek — bu `ISessionManager`'ın işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim oluşturur:** `ISessionManager.AddExchangeAsync()` — user/assistant çiftini kaydeder
- **Kim kullanır:**
  - `ReasoningMessageBuilder` — reasoning prompt'u için geçmişi okur
  - `WorkflowMessageBuilder` — agent workflow bağlamına geçmişi ekler
  - `EntityVerifier` — geçmiş turlardan entity çıkarır

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 `record` olarak tasarlanmış çünkü mesajlar immutable'dır — bir kez oluşturulduktan sonra değişmezler. `record` ayrıca `ToString()`, `Equals()` ve `GetHashCode()` methodlarını otomatik üretir.

## 6. Metotlar / Üyeler

### ConversationMessage

| Üye | Tip | Açıklama |
|-----|-----|----------|
| `Role` | `string` | Mesajın rolü (system / user / assistant) |
| `Text` | `string` | Mesaj içeriği |

### ConversationRoles (static sabitleri)

| Sabit | Değer | Açıklama |
| ------- | ------- | ---------- |
| `System` | `"system"` | Sistem prompt'u — LLM'e verilen talimat |
| `User` | `"user"` | Kullanıcının mesajı |
| `Assistant` | `"assistant"` | Bot'un yanıtı |

## 7. Constructor Bağımlılıkları

Yok — record tipi, `(string Role, string Text)` parametreleri constructor'dır.

## Bağlantılar

- [ChatBridgeMessage.md](ChatBridgeMessage.md) — Canlı sohbet UI mesajı (farklı amaç)
- [../../CustomerSupportBot.Application/ReasoningPipeline.md](../../CustomerSupportBot.Application/Services/Reasoning/ReasoningService.md) — Bu mesajları kullanan pipeline
