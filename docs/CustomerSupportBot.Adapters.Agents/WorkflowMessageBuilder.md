# WorkflowMessageBuilder

## Ne İşe Yarar
Workflow çalıştırıcısına (WorkflowRunner) giden system/user mesajlarını inşa eden sınıftır. Bağlam, reasoning özeti, entity hint ve replan notlarından oluşan mesaj listesini oluşturur.

## Hangi Amaçla Kullanılır
[WorkflowRunner](WorkflowRunner.md) bir agent workflow'u başlatmadan önce, LLM'e gönderilecek mesaj listesinin hazırlanması için kullanılır. #47 refactor'ı ile WorkflowRunner'dan ayrıştırılmıştır.

## Sorumlulukları
- Context pipeline'ından bağlam bilgisi çekmek (konuşma geçmişi, müşteri profili, semantik hafıza).
- Prompt repository'den sistem prompt'unu yüklemek.
- Reasoning sonuçlarından özet mesaj oluşturmak.
- Entity hint mesajları inşa etmek (IdExtractor üzerinden — "order_id MEVCUT" gibi ifadeler).
- Replan notu varsa ek sistem mesajı eklemek.
- Sipariş yönlendirme mesajını yeniden yazmak.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `IContextPipeline`, `IPromptRepository`, `IChatClient`, `ILoggerFactory`.
- **Kullanan sınıf**: [WorkflowRunner](WorkflowRunner.md).
- **İlişkili bileşenler**: `CustomerSupportBot.Application` → `ContextPipeline`, `IdExtractor`.
- **Prompt dosyaları**: `CustomerSupportBot.Api/Prompts/agents/*.md` — gizli sözleşme (entity hint metni ve mesaj sırası prompt'larla uyumlu olmalıdır).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
WorkflowRunner'ın SRP ihlali #47'de çözülerek mesaj inşası bu sınıfa taşınmıştır. Prompt dosyalarıyla belgesiz bir sözleşme vardır: entity hint metni ve mesaj sırası değiştirilirken ilgili prompt dosyalarının da gözden geçirilmesi gerekir.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning)` | Tam mesaj listesini oluşturur: system prompt, context, reasoning özeti, entity hints, replan notu, kullanıcı mesajı. |

## Bağımlılıklar
- `IContextPipeline` — Bağlam toplama.
- `IPromptRepository` — Prompt template yükleme.
- `IChatClient` — (Reasoning özeti için LLM çağrısı gerekebilir).
- `IdExtractor` (Domain) — Entity hint metni.
