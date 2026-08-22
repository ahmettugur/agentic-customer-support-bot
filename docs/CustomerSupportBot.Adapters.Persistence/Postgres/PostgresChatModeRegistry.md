# PostgresChatModeRegistry

**Dosya:** `Postgres/PostgresChatModeRegistry.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IChatModeRegistry`](../../CustomerSupportBot.Application/Ports/Outbound/Chat/IChatModeRegistry.md)

## 1. Ne İşe Yarar

Bir oturumun `Bot` mu `Human` (admin devraldı) modunda mı olduğunu `chat.chat_session_modes` tablosunda tutan, hibrit cache + distributed lock + Redis pub/sub adaptörü. [`PostgresChatBridge`](PostgresChatBridge.md) ile birlikte "canlı devralma" özelliğinin mod-yönetim yarısıdır.

## 2. Hangi Amaçla Kullanılır

Admin panelinde "sohbeti devral" butonu `TakeOver`'ı, "bota geri ver" `Release`'i çağırır. Chat orkestrasyonu her turda `GetMode`/`GetState` ile oturumun kimin elinde olduğunu kontrol eder (Human modundaysa bot cevap vermez).

## 3. Sorumlulukları

- Üstlendiği: mod geçişlerinin TEK bir admin tarafından, çakışmasız yapılmasını garanti etmek; cross-pod tutarlılık.
- Üstlenmediği: gerçek mesaj alışverişi (bkz. [`PostgresChatBridge`](PostgresChatBridge.md)).

## 4. İlişkiler

- `IChatModeRegistry` portunu implemente eder.
- `IAppDistributedLock`, `IMessageBusPort` (Redis `csbot:chatmode`), `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.

## 5. Tasarım Yaklaşımı

> 🐞 **`TakeOver`/`Release` neden DB-otoriter hale getirildi (`ReadStateFromDb`):** Eskiden karar salt yerel cache üzerinden veriliyordu ve distributed lock'a rağmen şu senaryo mümkündü: Admin A bir oturumu devralır, bu bilgi Redis pub/sub ile diğer pod'lara yayılır — ama bu mesaj kaybolursa, mesajı kaçıran pod'un cache'i "kimse devralmamış" der. O pod'a düşen Admin B'nin `TakeOver` isteği, kilit altında olsa bile, bayat cache'e bakıp devralmayı kabul eder ve Admin A'nın sahipliğini koşulsuz UPSERT ile SESSİZCE EZER. Kilit yalnızca AYNI ANDA gelen çağrıları serialize eder, sonradan gelen bayat bir kararı engellemez. Düzeltme: kilit altında karar HER ZAMAN `ReadStateFromDb` ile **DB'den taze okunarak** verilir — kayıtların gerçek kaynağı (source of truth) DB'dir, cache yalnızca bir hızlandırma katmanıdır.

> 🐞 **`ReadStateFromDb` neden DB okunamazsa cache'e düşmeden exception fırlatıyor:** "Okuyamamak" ile "sahip yok" birbirinden farklıdır — DB erişilemezken bayat cache'ten "sahip yok" sonucuna varıp devralmaya izin vermek, tam da bu düzeltmenin kapattığı hataya geri dönmek olurdu.

`Release`, `TakeOver` ile **aynı** `$"takeover:{sessionId}"` kilidini alır — böylece bir devralma ile bir bırakma asla yarışamaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ChatMode GetMode(string sessionId)` | Cache'ten; kayıt yoksa varsayılan `Bot`. |
| `ChatSessionState? GetState(string sessionId)` | Cache'ten tam durum nesnesi. |
| `bool TakeOver(string sessionId, string? humanAgent)` | Distributed lock altında DB'den taze durumu okur; başka bir admin zaten devralmışsa `false`; aksi hâlde `Human` moduna geçirir, DB + cache + Redis günceller. |
| `bool Release(string sessionId)` | Aynı kilit altında DB'den okur, `Bot` moduna döner. Zaten Bot modundaysa `false`. |
| `IReadOnlyList<ChatSessionState> GetActive()` | Cache'ten `Human` modundaki tüm oturumlar, en yeni devralınan önce. |
| `event EventHandler<ChatSessionState>? ModeChanged` | Mod her değiştiğinde (yerel veya uzak pod'dan) tetiklenir. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `IAppDistributedLock`
- `ILogger<PostgresChatModeRegistry>`

## Bağlantılar

- [IChatModeRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Chat/IChatModeRegistry.md)
- [PostgresChatBridge](PostgresChatBridge.md) — mesaj alışverişi (bu sınıfın tamamlayıcısı)
- [PostgresApprovalQueue](PostgresApprovalQueue.md) — benzer "DB-otoriter karar" deseni
