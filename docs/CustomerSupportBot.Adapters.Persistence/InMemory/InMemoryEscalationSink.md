# InMemoryEscalationSink

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryEscalationSink.cs`
- **Port:** `IEscalationSink`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

İnsan temsilciye devir (escalation) kayıtlarını `ConcurrentDictionary` + `ConcurrentQueue` (ring buffer, kapasite 500) ile bellekte tutan depodur.

## 2. Hangi Amaçla Kullanıldığı

`PostgresEscalationSink`'in tek-process karşılığı. `HumanHandoffAgent`'ın oluşturduğu escalation isteklerinin durum makinesini (`Open`/`Acknowledged`/`Resolved`/`Dismissed`) yönetir.

## 3. Sorumlulukları

- Escalation oluşturma (`Create`) ve `RequestCreated` event'i fırlatma.
- Açık/onaylanmış istekleri listeleme (`GetOpen`), en son isteklerin genel dökümü (`GetRecent`), bir temsilciye ait olanlar (`GetRecentForAgentAsync`).
- Karar işleme (`Decide`) — aksiyon string'ini normalize eder (`acknowledge`/`ack`/`resolve`/`dismiss`), gerçek geçiş mantığını **kendi içinde değil**, `EscalationStateFactory.Create(req.Status)`'un döndürdüğü duruma özel state nesnesine (`EscalationStates`, Domain katmanı) delege eder.
- Kapasite aşımında en eski kayıtları düşürme (`Trim`).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresEscalationSink` (`../Postgres/HitlAndChat.md`) ile aynı arayüzü uygular.
- Durum geçiş mantığı için Domain katmanındaki `EscalationStateFactory`/`EscalationStates`'e (State Pattern) delege eder — bu adaptör sadece depolama ve event fırlatma yapar, iş kuralı burada YOK.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Karar mantığını State Pattern'e (Domain) devretmek, aynı geçiş kurallarının hem Postgres hem InMemory implementasyonunda **tekrarlanmadan** tek yerde (Domain, framework'ten bağımsız, test edilebilir) yaşamasını sağlar — DRY ilkesi.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `event RequestCreated` / `event RequestDecided` | SSE/admin panel bildirimleri. |
| `Create(request)` | Kaydeder, kapasiteyi budar, event fırlatır. |
| `GetOpen()` | `Open`/`Acknowledged` durumundakiler, oluşturulma sırasına göre. |
| `GetRecentForAgentAsync(agentId, count, ct)` | Belirli bir temsilciye atanmış (veya hiç atanmamış) son istekler. |
| `GetRecent(count)` | Durumdan bağımsız son istekler. |
| `Get(id)` | Tek kayıt. |
| `Decide(id, action, assignedTo, resolution)` | Aksiyonu normalize edip ilgili `EscalationStates` state nesnesine delege eder; başarısızsa `false`. |

## 7. Bağımlılıklar

- `ILogger<InMemoryEscalationSink>`
- (Metot içi) `EscalationStateFactory` — Domain katmanı, DI ile değil doğrudan çağrı ile kullanılır.
