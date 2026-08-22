# InMemorySlaEventSink

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemorySlaEventSink.cs`
- **Port:** `ISlaEventSink`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

SLA (hizmet seviyesi anlaşması) ihlali/uyarı olaylarını (`SlaEvent`) `ConcurrentQueue<SlaEvent>` ring buffer'ında (kapasite 500) tutar; ayrıca hedef+önem-derecesi başına son yayınlanma zamanını (`_lastEmittedAt`) izler.

## 2. Hangi Amaçla Kullanıldığı

`PostgresSlaEventSink`'in tek-process karşılığı. `SlaGuardianService`'in (Api katmanı, periyodik) ürettiği SLA olaylarını saklar.

## 3. Sorumlulukları

- `Record` — olayı kuyruğa ekler, kapasiteyi kırpar, `_lastEmittedAt`'i günceller, loglar, `EventRecorded` event'i fırlatır.
- `GetRecent(count)` — son N olay.
- `LastEmittedAt(kind, targetId, severity)` — aynı (tür, hedef, önem-derecesi) kombinasyonu için en son ne zaman olay üretildiğini döner; bu, **çağıran tarafın** (`SlaGuardianService`) aynı ihlali kısa aralıklarla tekrar tekrar bildirmesini önlemesine yardımcı olur (debounce mantığı burada değil, çağıranda uygulanır — bu sınıf sadece son zamanı raporlar).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresSlaEventSink` (`../Postgres/StoresAndSinks.md`) ile aynı arayüzü uygular.
- `SlaEvent` modeli Domain katmanındadır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`Key(kind, targetId, severity)` bileşik anahtarı — üç boyutlu bir "son görülme zamanı" haritasını tek boyutlu bir `ConcurrentDictionary`'ye indirger; `string` birleştirme basit ama yeterlidir (yüksek hacimli değil, SLA olayları doğası gereği seyrek).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `event EventRecorded` | Yeni olay kaydedildiğinde tetiklenir. |
| `Record(evt)` | Kaydeder, kapasiteyi kırpar, `_lastEmittedAt`'i günceller. |
| `GetRecent(count)` | Son `count` olay (varsayılan 100), ters kronolojik. |
| `LastEmittedAt(kind, targetId, severity)` | Son yayın zamanı, hiç yoksa `null`. |

## 7. Bağımlılıklar

- `ILogger<InMemorySlaEventSink>`
