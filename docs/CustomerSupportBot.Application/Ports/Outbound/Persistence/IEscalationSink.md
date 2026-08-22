# IEscalationSink

**Kaynak:** `Ports/Outbound/Persistence/IEscalationSink.cs`
**İmplementasyonlar:** [`InMemoryEscalationSink`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryEscalationSink.md), [`PostgresEscalationSink`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresEscalationSink.md)

## 1. Ne İşe Yarar

İnsan temsilciye devir (eskalasyon) kayıtları için secondary port.

## 2. Hangi Amaçla Kullanılır

Bir konuşma insan müdahalesi gerektirdiğinde `Create` ile eskalasyon açılır;
[`ISkillsBasedRouter`](../ISkillsBasedRouter.md) uygun temsilciyi önerir; admin/agent panelleri
`GetOpen`/`GetRecentForAgentAsync` ile kuyruğu görüntüler; `Decide` ile kabul/red/çözüm kararı
verilir.

## 3. Sorumlulukları

- **Üstlendiği:** Eskalasyon kaydı CRUD'u ve durum makinesi (`Decide`).
- **Üstlenmediği:** Hangi temsilcinin uygun olduğuna karar vermek — o
  [`ISkillsBasedRouter`](../ISkillsBasedRouter.md)'ın işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`InMemoryEscalationSink`/`PostgresEscalationSink` implemente eder;
[`IHumanAgentRegistry`](IHumanAgentRegistry.md) ile birlikte kullanılır (`IncrementLoad`/
`DecrementLoad` çağrıları eskalasyon atama/kapanışına eşlik eder).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`GetRecentForAgentAsync` neden async ve ayrı bir metot:** diğer okumaların aksine bu
> metot cache üzerinden cevaplanamaz. `GetRecent` yalnızca hydrate edilmiş kayıtları görür
> (Postgres adaptöründe: açık olanlar + son N kapalı); bir agent'ın kendi kapalı kaydı o
> pencerenin gerisinde kalabilir. Filtreyi cache üzerinde uygulamak sınırı ötelemekten
> ibarettir — hem daraltma hem limit veri kaynağında yapılmalıdır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `EscalationRequest Create(EscalationRequest request)` | Yeni eskalasyon oluşturur. |
| `IReadOnlyList<EscalationRequest> GetOpen()` | Açık (kapanmamış) tüm eskalasyonlar. |
| `IReadOnlyList<EscalationRequest> GetRecent(int count = 50)` | Son N eskalasyon (cache). |
| `EscalationRequest? Get(string id)` | Tekil sorgu. |
| `Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(string agentId, int count = 50, CancellationToken ct = default)` | Bir agent'ın görebileceği son N eskalasyon (kalıcı, doğru daraltma). |
| `bool Decide(string id, string action, string? assignedTo = null, string? resolution = null)` | Atama/kabul/çözüm kararı işler. |
| `event EventHandler<EscalationRequest>? RequestCreated` | Yeni kayıt olduğunda fırlar. |
| `event EventHandler<EscalationRequest>? RequestDecided` | Karar verildiğinde fırlar. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.EscalationRequest`'e bağımlıdır.
