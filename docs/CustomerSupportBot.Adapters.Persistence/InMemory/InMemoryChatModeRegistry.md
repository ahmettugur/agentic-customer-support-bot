# InMemoryChatModeRegistry

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryChatModeRegistry.cs`
- **Port:** `IChatModeRegistry`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

Bir sohbet oturumunun `Bot` mu yoksa `Human` (admin devraldı) modunda mı olduğunu bellekte (`ConcurrentDictionary<string, ChatSessionState>`) tutan registry'dir.

## 2. Hangi Amaçla Kullanıldığı

`PostgresChatModeRegistry`'nin tek-process karşılığıdır. Admin panelinden "Üstlen"/"Bırak" aksiyonlarının anlık, race-condition'suz uygulanmasını sağlar.

## 3. Sorumlulukları

- Mod okuma (`GetMode`, `GetState`).
- Devralma (`TakeOver`) — eşzamanlı ikinci bir devralmayı engeller: session zaten BAŞKA bir admin tarafından `Human` moddaysa `false` döner; aynı admin tekrar çağırırsa (reconnect senaryosu) izin verilir.
- Bırakma (`Release`) — modu `Bot`'a döndürür.
- Aktif (Human moddaki) oturumları listeleme (`GetActive`).
- `ModeChanged` event'i ile ilgilenen taraflara (SSE) bildirim.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresChatModeRegistry` (`../Postgres/PostgresChatModeRegistry.md`) ile aynı `IChatModeRegistry` arayüzünü uygular.
- `InMemoryChatBridge` ile birlikte kullanılır — bridge mesajı hangi yöne göndereceğine bu registry'nin döndüğü moda bakarak karar vermez (bu karar tüketici tarafında, ör. `ChatPortService`'te), registry sadece durumu tutar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`TakeOver`'daki "aynı admin ise izin ver, farklı admin ise reddet" mantığı, iki admin'in aynı anda aynı müşteriyle konuştuğunu sanmasını (ve birbirinin cevaplarını görmemesini) önleyen concurrent-takeover koruması içindir — `ConcurrentDictionary.AddOrUpdate`'in atomik `add`/`update` delegeleriyle race'siz uygulanır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `event ModeChanged` | Mod değiştiğinde tetiklenir. |
| `GetMode(sessionId)` | Session bulunamazsa varsayılan `ChatMode.Bot` döner. |
| `GetState(sessionId)` | Tam `ChatSessionState` nesnesini döner (bulunamazsa `null`). |
| `TakeOver(sessionId, humanAgent)` | `humanAgent` boşsa `WellKnown.Defaults.Admin` kullanılır; zaten başka bir admin tarafından alınmışsa `false`. |
| `Release(sessionId)` | Zaten `Bot` moddaysa `false`; aksi halde modu sıfırlar. |
| `GetActive()` | `Human` moddaki tüm oturumları, en son devralınana göre sıralı döner. |

## 7. Bağımlılıklar

- `ILogger<InMemoryChatModeRegistry>`
