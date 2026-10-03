# InMemoryEscalationSink

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryEscalationSink.cs`
- **Port:** `IEscalationSink`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

İnsan temsilciye devir (escalation) kayıtlarını `ConcurrentDictionary` + `ConcurrentQueue` (ring buffer, kapasite 500) ile bellekte tutan depodur.

## 2. Hangi Amaçla Kullanıldığı

`PostgresEscalationSink`'in tek-process karşılığı. `HumanHandoffAgent`'ın oluşturduğu escalation isteklerinin durum makinesini (`Open`/`Acknowledged`/`Resolved`/`Dismissed`) yönetir.

## 3. Sorumlulukları

- Escalation oluşturma (`Create`) ve `RequestCreated` event'i fırlatma. Aynı session + ajan için açık (`Open`/`Acknowledged`) kayıt varsa yenisini oluşturmaz, **mevcut kaydı** döndürür (Postgres'teki `ux_escalations_open_session_agent` ile aynı kural; session'sız kayıtlar dedup'a girmez).
- Açık/onaylanmış istekleri listeleme (`GetOpen`), en son isteklerin genel dökümü (`GetRecent`), bir temsilciye ait olanlar (`GetRecentForAgentAsync`).
- Karar işleme (`Decide`) — aksiyon string'ini normalize eder (`acknowledge`/`ack`/`resolve`/`dismiss`), gerçek geçiş mantığını **kendi içinde değil**, `EscalationStateFactory.Create(req.Status)`'un döndürdüğü duruma özel state nesnesine (`EscalationStates`, Domain katmanı) delege eder.
- Kapasite aşımında en eski kayıtları düşürme (`Trim`).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresEscalationSink` (`../Postgres/PostgresEscalationSink.md`) ile aynı arayüzü uygular.
- Durum geçiş mantığı için Domain katmanındaki `EscalationStateFactory`/`EscalationStates`'e (State Pattern) delege eder — bu adaptör sadece depolama ve event fırlatma yapar, iş kuralı burada YOK.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Karar mantığını State Pattern'e (Domain) devretmek, aynı geçiş kurallarının hem Postgres hem InMemory implementasyonunda **tekrarlanmadan** tek yerde (Domain, framework'ten bağımsız, test edilebilir) yaşamasını sağlar — DRY ilkesi.

`Create`'in "açık kayıt var mı?" kontrolü ile eklemesi ve `Decide`'ın durum okuması ile geçişi tek bir kilit (`_gate`) altında yapılır. Postgres adaptöründe aynı garantiyi unique index ve koşullu UPDATE verir; tek süreçli bu adaptörde kilit yeterlidir. Böylece iki adaptörün eşzamanlılık sözleşmesi aynıdır: aynı session + ajan için tek açık kayıt, yarışan iki karardan yalnızca biri `true`.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `event RequestCreated` / `event RequestDecided` | SSE/admin panel bildirimleri. |
| `CreateAsync(request)` | Kaydeder, kapasiteyi budar, event fırlatır. Aynı session + ajan için açık kayıt varsa onu döndürür (event yok). |
| `GetOpen()` | `Open`/`Acknowledged` durumundakiler, oluşturulma sırasına göre. |
| `GetRecentForAgentAsync(agentId, count, ct)` | Belirli bir temsilciye atanmış (veya hiç atanmamış) son istekler. |
| `GetRecent(count)` | Durumdan bağımsız son istekler. |
| `Get(id)` | Tek kayıt. |
| `DecideAsync(id, action, assignedTo, resolution)` | Kilit altında aksiyonu normalize edip ilgili `EscalationStates` state nesnesine delege eder; başarısızsa `false`. |

## 7. Bağımlılıklar

- `ILogger<InMemoryEscalationSink>`
- (Metot içi) `EscalationStateFactory` — Domain katmanı, DI ile değil doğrudan çağrı ile kullanılır.
