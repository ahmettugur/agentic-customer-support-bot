# ReasoningService

- **Kaynak:** `Services/Reasoning/ReasoningService.cs`
- **Tür:** `public class : IReasoningPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## 1. Ne İşe Yarar

İki aşamalı ReAct akışının **birinci aşamasıdır**: kullanıcı sorgusu için açık (explicit)
reasoning — niyet, gerekli bilgiler, güven skoru, alt görevler — üretir. Ana agent-team
workflow'undan **bağımsız** çalışır: önce reasoning, sonucu workflow'a girdi olarak geçer.

## 2. Hangi Amaçla Kullanılır

Ajanların "körlemesine" cevap üretmesi yerine, önce açık bir muhakeme adımından geçmesini
sağlamak: niyeti belirlemek, hangi entity'lerin (`order_id`, `customer_id`...) DB'de gerçekten
var olduğunu doğrulamak (`EntityVerifier`), tutarsızlıkları erkenden yakalamak
(`ReasoningSanityChecker`), ve gerekirse compound sorguları alt görevlere bölmek.

## 3. Sorumlulukları

**Üstlendiği:**
- `EntityVerifier.Verify` ve `ReasoningMessageBuilder.Build` ile prompt'u hazırlamak.
- Reasoning LLM çağrısını (`IReasoningChatClient`) **kendi zaman aşımı bütçesiyle** (workflow'dan
  bağımsız) çalıştırmak.
- LLM çıktısını `ReasoningResultParser.Parse` ile ayrıştırmak, `ReasoningSanityChecker.Check`
  ile denetlemek, `VerifiedEntities`'i sonuca eklemek.
- Hem non-streaming (`ReasonAsync`) hem streaming (`ReasonStreamingAsync`) arayüz sağlamak —
  gerçek arayüz (`/chat/stream`) streaming yolu kullanır.
- Reasoning tamamen başarısız olsa bile **boş/fallback bir `ReasoningResult` ile devam etmek** —
  reasoning hiçbir zaman turu tamamen durdurmaz.

**Üstlenmediği:** Entity doğrulama mantığı ([`EntityVerifier`](EntityVerifier.md)'ın işi),
tutarsızlık kuralları ([`ReasoningSanityChecker`](ReasoningSanityChecker.md)'ın işi), prompt
metninin kurulması ([`ReasoningMessageBuilder`](ReasoningMessageBuilder.md)'ın işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IReasoningChatClient` — reasoning için kullanılan (genelde daha hafif/hızlı) LLM istemcisi.
- `EntityVerifier`, `ReasoningSanityChecker`, `ReasoningMessageBuilder` (kendi `new`'lediği,
  DI'sız) — bkz. madde 7.
- `WorkflowGuardOptions.ReasoningTimeoutSeconds` — bu servise özgü zaman aşımı bütçesi.
- Tüketicisi: `IAgentTeamPort`/`WorkflowRunner` (Adapters.Agents) — reasoning sonucu
  workflow'a girdi olarak geçilir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### Neden kendi zaman aşımı bütçesi var

> 🐞 **Reasoning workflow'dan ÖNCE çalışır** ve `WorkflowGuardOptions.TimeoutSeconds` yalnızca
> workflow'u kapsar — yani asılı kalan bir reasoning çağrısının hiçbir bütçesi yoktu.
> `ReasoningTimeoutSeconds` bunun için eklendi: süre dolarsa `catch` bloğu devreye girer ve tur
> **boş reasoning ile devam eder** — niyet çıkarımı kaybolur ama kullanıcı yanıtsız kalmaz.

### Streaming yolda da aynı timeout neden gerekli

> 🐞 Bu eklenmeden önce yalnızca non-streaming `ReasonAsync` korunuyordu — oysa asıl arayüz
> `/chat/stream` streaming yolunu kullanıyor, yani pratikte **korunmayan yol** buydu: asılı
> kalan bir reasoning akışı hiçbir bütçeye tabi değildi.

### İki ayrı `CancellationToken` kaynağının ayrıştırılması (`EnumerateSafely`)

Çağıranın iptali (kullanıcı bağlantıyı kapattı) ile bütçe timeout'u **ayrıştırılmak zorunda**:
biri yukarı fırlatılmalı (`OperationCanceledException` tekrar throw edilir), diğeri fallback'e
dönüşmelidir. Tek token'a bakılsaydı timeout da "çağıran iptal etti" sayılır ve tur cevapsız
kalırdı.

### Chunk throttling — `Task.Delay` neden kaldırıldı

Emit'ler `minInterval=20ms`'den sık olmaz, arada gelen chunk'lar `pendingChunks`'ta birikir.

> 🐞 Eskiden `elapsed < minInterval` durumunda ekstra bir `Task.Delay(minInterval)` vardı —
> bu KALDIRILDI çünkü `elapsed` kontrolü zaten throttling yapıyordu; ek bekleme yalnızca akışı
> yapay olarak yavaşlatıyor, hiçbir chunk'ı biriktirmiyordu (kullanıcı yanıtı gereksiz yere geç
> görüyordu).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ReasonAsync(query, session, history?, ct)` | Non-streaming: verify → prompt → LLM çağrısı (kendi timeout'uyla) → parse → sanity check → `VerifiedEntities` ekle. `catch` bloğunda `Confidence=Low`, `ConfidenceScore=0.3` ile fallback `ReasoningResult` döner — turu asla düşürmez. Çağıranın kendi iptali (`ct.IsCancellationRequested`) ayrı yakalanıp yukarı fırlatılır. |
| `ReasonStreamingAsync(query, session, history?, ct)` | Streaming: `ReasoningStart` event'i → verify/prompt → `IReasoningChatClient.StreamAsync`'i `EnumerateSafely` ile güvenli tüketir → throttle'lı `ReasoningDelta` event'leri yayınlar → timeout/hata/boş metin durumunda fallback sonuç, aksi halde `ReasoningResultParser.Parse` → `ReasoningComplete` event'i ile nihai `ReasoningResult`'ı yayınlar. |
| `EnumerateSafely(stream, ct, callerCt)` *(private static)* | Stream'i tüketirken hataları `(chunk, error)` tuple'ına çevirir; `callerCt` iptaliyle biten `OperationCanceledException`'ı tekrar fırlatır, diğer tüm hataları veri olarak döner. |

## 7. Bağımlılıklar

Constructor injection ile: `IReasoningChatClient`, `ILogger<ReasoningService>`,
`IPromptRepository`, `EntityVerifier`, `ReasoningSanityChecker`,
`IOptions<WorkflowGuardOptions>`. **`ReasoningMessageBuilder` DI'dan gelmez** — constructor
içinde `new ReasoningMessageBuilder(prompts)` ile elle oluşturulur (stateless, saf bir
formatlayıcı olduğu için DI kaydına gerek görülmemiş).

## Bağlantılar

- [EntityVerifier.md](EntityVerifier.md), [ReasoningSanityChecker.md](ReasoningSanityChecker.md), [ReasoningMessageBuilder.md](ReasoningMessageBuilder.md) — bu servisin doğrudan kullandığı 3 bileşen
- [SubTaskOrchestrator.md](SubTaskOrchestrator.md) — reasoning sonucundaki `SubTasks`'ın ikinci aşamada nasıl işlendiği
