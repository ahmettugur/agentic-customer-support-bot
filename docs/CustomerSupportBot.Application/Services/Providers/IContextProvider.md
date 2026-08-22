# IContextProvider

- **Kaynak:** `CustomerSupportBot.Application/Services/Providers/IContextProvider.cs`
- **Tür:** `public interface`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## Ne işe yarar?

`IContextProvider`, [ContextPipeline](../Chat/ContextPipeline.md) boru hattında çalışan tüm dinamik bağlam sağlayıcıların (Konuşma Özeti, Müşteri Profili, RAG Semantik Bellek, Ürün Önerisi) uygulaması gereken standart sözleşmedir.

## Hangi amaçla kullanılır`?

- Çoklu ajan iş akışında prompt'a eklenecek farklı bağlam kaynaklarını modüler ve sıralı (`Order`) olarak çalıştırmak.
- Her sağlayıcının kendi sorumluluğunu izole bir şekilde yerine getirmesini sağlamak.

## Metotlar ve Özellikler

### Özellikler

- `Name` (`string`): Sağlayıcının tekil adı (örn: `"ConversationSummary"`, `"SemanticMemory"`).
- `Order` (`int`): Boru hattındaki çalışma sırası (Küçük sayılar önce çalışır).

### Metotlar

#### `GetContextAsync`
```csharp
Task<string?> GetContextAsync(
    AgentSession session,
    string currentQuery,
    CancellationToken ct = default);
```
- **Ne işe yarar?:** Verilen oturum ve anlık kullanıcı sorgusu için bağlam metni üretir. Eklenecek bir bağlam yoksa `null` döner.

## Bağımlılıklar

- [AgentSession](../../../CustomerSupportBot.Domain/Model/AgentSession.md)
