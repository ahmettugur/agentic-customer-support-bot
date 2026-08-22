# ConversationSummaryProvider

- **Kaynak:** `CustomerSupportBot.Application/Services/Providers/ConversationSummaryProvider.cs`
- **Tür:** `public class : IContextProvider`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## Ne işe yarar?

`ConversationSummaryProvider`, konuşma geçmişi belirli bir eşiği (`SummaryThreshold = 8` mesaj) aştığında, eski mesajları LLM kullanarak **artımlı (incremental folding)** olarak özetleyen ve mevcut tur için özet ile en son mesajları (`RecentMessageCount = 4`) bağlam olarak üreten bağlam sağlayıcıdır.

## Hangi amaçla kullanılır`?

- Uzun sohbet oturumlarında token tüketimini ve gecikmeyi (latency) minimumda tutmak.
- **Artımlı Özetleme Mantığı (Folding):** Her turda tüm geçmişi baştan özetlemek yerine, sadece son özetten bu yana aralık dışına düşen yeni mesajları (`FoldAsync`) mevcut özetle birleştirerek gereksiz LLM token maliyetini önlemek.
- Dağıtık kilit (`_distributedLock`) ile aynı oturumda eşzamanlı iki turun aynı anda özet üretmesini engellemek.
- `IContextSanitizer` ile üretilen özeti prompt enjeksiyonuna karşı sterilize etmek.

## Sorumlulukları

- **Üstlendiği:**
  - `Name = "ConversationSummary"` ve `Order = 5` ile `ContextPipeline` içerisinde doğru sırada çalışmak.
  - `GetContextAsync` ile geçmişi incelemek; mesaj sayısı 8'den azsa `null`, 8 ve üzerindeyse artımlı özeti hesaplayıp markdown bloğu olarak dönmek.
  - Oturum durumundaki `Summary` ve `LastSummarizedMessageIndex` alanlarını güncellemek.
- **Üstlenmediği:** Ham sohbet geçmişini doğrudan silmek (geçmiş veritabanında tam olarak kalır, sadece LLM prompt'una özetlenmiş olarak sunulur).

## Constructor ve Başlatma Mantığı

```csharp
public ConversationSummaryProvider(
    IGeneralChatClient chatClient,
    ISessionManager sessionRepository,
    IAppDistributedLock distributedLock,
    IContextSanitizer sanitizer,
    ILogger<ConversationSummaryProvider> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_chatClient`: Özetleme için kullanılan hafif LLM istemcisi.
- `_sessionRepository`: Oturum mesaj geçmişini (`GetHistoryAsync`) okumak için kullanılır.
- `_distributedLock`: Oturum bazlı kilit mekanizması (`IAppDistributedLock`).
- `_sanitizer`: Çıktı temizleme arayüzü (`IContextSanitizer`).
- `_logger`: Günlükleme motoru.

## Metotlar ve İç Çalışma Mantıkları

### 1. `GetContextAsync`
```csharp
public async Task<string?> GetContextAsync(
    AgentSession session,
    string currentQuery,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Oturum için mevcut özeti döner veya gerekiyorsa yeni mesajları katlayarak günceller.
- **İç Mantığı:**
  1. `_sessionRepository.GetHistoryAsync(session.SessionId, ct)` ile mesaj geçmişi okunur.
  2. `history.Count < SummaryThreshold (8)` ise `null` döner (özete henüz gerek yoktur).
  3. `cutoffIndex = history.Count - RecentMessageCount (4)` hesaplanır.
  4. Son özetlenen indeks (`session.State.LastSummarizedMessageIndex`), `cutoffIndex` değerinden küçükse `FoldAsync` çağrılarak yeni düşen mesajlar mevcut özetle birleştirilir.
  5. Formatlanmış `## 📝 Konuşma Özeti\n{özet}` metni döndürülür.

### 2. `FoldAsync` (Private)
- **Ne işe yarar?:** Mevcut özet ile yeni sınır dışına çıkan mesajları LLM'e tek bir prompt olarak verip güncel özet metnini üretir.

## Özellikler/Properties

- `Name` (`string`): Sabit `"ConversationSummary"`.
- `Order` (`int`): `5` (Müşteri bağlamından sonra, semantik bellekten önce).

## Bağımlılıklar

- [IContextProvider](IContextProvider.md)
- [IContextSanitizer](../Memory/ContextSanitizer.md)
- `CustomerSupportBot.Application.Ports.Outbound.AI.IGeneralChatClient`
- `CustomerSupportBot.Application.Ports.Outbound.Persistence.ISessionManager`
- `CustomerSupportBot.Application.Ports.Outbound.Locking.IAppDistributedLock`
