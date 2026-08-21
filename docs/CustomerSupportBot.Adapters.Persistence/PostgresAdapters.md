# Postgres Adaptörler

`Postgres/` altındaki tüm adaptörler **Hybrid Cache + DB** deseni uygular. Her biri InMemory karşılığı ile aynı port'u implement eder; ek olarak PostgreSQL kalıcılığı ve opsiyonel Redis pub/sub ekler.

> 💡 **Analiz notu:** Bu dosya projedeki 17 Postgres adaptörünün hepsinin nasıl çalıştığını açıklar. Her adapter aynı deseni tekrar eder: "cache'ten oku, DB'ye yaz, Redis ile diğer pod'lara haber ver". Yeni bir adapter eklerken bu deseni takip edin.

Temel desen için önce [HybridPattern.md](HybridPattern.md) oku.

---

## PostgresSessionManager

**Port:** `ISessionManager`

**Yazma:** `AddExchangeAsync(sessionId, query, response, signals?)` → cache + DB INSERT (2 satır: user + assistant), ardından `ExtractAndUpdateStateCoreAsync` çağrılır.

**Hydration:** `GetAll()` çağrısında son 500 session metadata yüklenir. Tekil session `GetOrCreate`'te lazy yüklenir.

**`AppendAssistantMessage` özelliği:** Son mesaj boş asistan mesajı ise onu replace eder — streaming sırasında placeholder yazılmış olabilir.

**`ExtractAndUpdateStateCoreAsync`:** `SessionStateExtractor.ExtractAndApply`'ı (Domain) `session` nesnesi üzerinde `lock` altında çağırır, ardından `chat.sessions.state` JSONB günceller. Bu, turun türetilmiş state'inin (intent, sentiment, `ConsecutiveNegativeTurns`) **tek yazarıdır** — `signals` parametresi (`TurnSignals?`) reasoning'in bu tur için ürettiği intent/sentiment'i buraya girdi olarak taşır; `null` ise kural tabanlı çıkarıma düşülür. Kilit, `ConsecutiveNegativeTurns`'ün eşzamanlı isteklerde (çift-submit, çoklu sekme) bir artışı kaybetmesini önler. Detay: [`SessionStateExtractor.md`](../CustomerSupportBot.Domain/Services/SessionStateExtractor.md).

**Eşzamanlı ilk hydrate:** `EnsureSessionHydratedAsync`'teki değer bir bayrak değil, işin kendisidir (`ConcurrentDictionary<string, Lazy<Task>>`). Eskiden bayrak DB okuması BAŞLAMADAN konuyordu — ikinci eşzamanlı çağrı hemen dönüp boş bir `SessionState` ile devam ediyordu; o boş state üzerinden yapılan bir yazma, DB'deki müşteri sahipliğini `{}` ile eziyordu. Artık ikinci çağıran aynı `Task`'ı bekler; hydrate bir kez çalışır.

**Redis history mesajı kaybının uzlaştırılması:** Konuşma geçmişi pod'lar arasında DELTA olarak `csbot:session:history` kanalında yayılır (bkz. `PublishHistoryAppended`). Pub/sub en fazla bir kez teslim eder — bir mesaj kaybolursa cache o andan itibaren kalıcı olarak eksik kalırdı, çünkü hydrate yalnızca session ilk görüldüğünde bir kez çalışır. `GetHistoryAsync` artık her çağrıda ucuz bir SAYIM karşılaştırması yapar (`ReconcileHistoryIfStaleAsync` — indeksli `COUNT(*)`); yerel sayı DB'nin gerisindeyse yalnızca o session'ın mesaj listesi DB'den tam olarak yeniden yüklenir. Her turda tüm metni çekmek (approval'daki `GetPendingAsync` deseni) bu yolun sıklığında (her tur) gereksiz maliyetli olurdu — bu yüzden sayım-önce yaklaşımı seçildi.

---

## PostgresApprovalQueue

**Port:** `IApprovalQueue`

**Hydration:** Son 200 kayıt lazy yüklenir.  
**Yazma stratejisi:** Durable-first — DB INSERT/UPDATE önce, cache sonra.  
**Dağıtık lock:** `Decide()` çağrısında `IAppDistributedLock.TryAcquireAsync("approval:{id}")` — birden fazla pod aynı anda karar veremez.  
**Mükerrer yürütme koruması:** Kilit yalnızca *eş zamanlı* çağrıları serialize eder, bayat bir cache yüzünden *sonradan* gelen ikinci kararı değil. Asıl koruma `ClaimDecisionAsync`'tir: karar, gerçek iş yürütülmeden **önce** tek bir `UPDATE ... WHERE status = 'Pending'` ile sahiplenilir. 0 satır etkilenirse kararı başkası vermiştir; yürütme atlanır ve pod kendi cache'ini DB'den tazeler.  
**Redis:** `csbot:approval:created` / `csbot:approval:decided` kanalları. `decided` mesajı, dinleyen pod'da bekleyen bir `TaskCompletionSource` olup olmadığına **bakılmaksızın** işlenir — bloklamayan modelde kararı vermeyen pod'ların hiçbirinde TCS yoktur; TCS'e bağlanmak kararın yayılmasını, SSE bildirimini ve "sonradan gir de gör" akışını tümden engelliyordu.  
**Yürütme durumu:** Karar (`status`) ile gerçek işin sonucu (`execution_status`) ayrı kolonlardır. Onay claim'i `execution_status='Running'` yazar, iş bitince `Succeeded`/`Failed` olur — böylece yürütme sırasında süreç kapanırsa kayıt DB'de askıda GÖRÜNÜR kalır. Otomatik retry yoktur (tool'lar idempotent değil); bu kayıtlar admin panelinde işaretlenir.  
**Startup:** Onay kayıtlarına dokunulmaz (bkz. `PersistenceHydrator.md`). Süresi geçenler `StaleApprovalSweepService` tarafından periyodik olarak reddedilir.

---

## PostgresChatBridge

**Port:** `IChatBridge`

**Hydration:** Per-session lazy (session ilk abonelikte yüklenir).  
**Yazma:** BotTyping hariç tüm mesajlar DB'ye yazılır (audit trail).  
**Redis:** `csbot:bridge:touser` / `csbot:bridge:toadmin` — multi-pod live chat.  
**Reset:** In-memory geçmişi temizler; DB kayıtlarına dokunmaz (audit korunur).  
**Abonelik kaydı:** Session başına `ConcurrentDictionary<Channel<ChatBridgeMessage>, byte>` — bir küme olarak kullanılır. Eskiden `ConcurrentBag` idi ve abonelik sona erdiğinde (WebSocket/SSE kapanışı) yalnızca `Writer.TryComplete()` çağrılıyor, kanal koleksiyondan hiç çıkarılmıyordu — `ConcurrentBag` zaten tekil eleman silmeyi desteklemez. Sık bağlanıp kopan bir oturumda bu, tamamlanmış-ama-hâlâ-tutulan kanalların process ömrü boyunca birikmesi ve her `Broadcast`'in bu ölü kanalları da taraması demekti. `Unregister` artık aboneliğin `finally` bloğunda `TryRemove` ile kaydı gerçekten temizler. Aynı kusur (ve aynı düzeltme) `InMemoryChatBridge`'de de var.

---

## PostgresChatModeRegistry

**Port:** `IChatModeRegistry`

**Hydration:** Tüm tablo lazy full-hydration.  
**Yazma:** UPSERT `chat.session_modes`.  
**Dağıtık lock:** `TakeOver()` VE `Release()` sırasında (ikisi de) `IAppDistributedLock.TryAcquireAsync("takeover:{sessionId}")` — eş zamanlı iki çağrı önlenir.  
**Karar DB'den:** Kilit altında sahiplik kararı `ReadStateFromDb` ile kayıtların gerçek kaynağından verilir, yerel cache'ten DEĞİL. Eskiden `TakeOver` kilit alsa da kararı bayat yerel cache'ten veriyordu — devralma bilgisi pod'lara Redis pub/sub ile ulaşır ve o mesaj kaybolabilir; mesajı kaçıran pod cache'inde hiçbir sahip görmez, devralmayı kabul eder ve koşulsuz UPSERT ile aktif admin'i ezerdi. `Release` ise kilidi HİÇ almıyordu ve aynı bayat cache'ten karar veriyordu — başka bir admin'in aktif oturumunu serbest bırakabiliyordu. DB okunamazsa cache'e düşülmez (okuyamamak "sahip yok" anlamına gelmez).  
**Redis:** `csbot:chatmode` kanalı — mod değişikliği broadcast.

---

## PostgresEscalationSink

**Port:** `IEscalationSink`

**Hydration:** Açık eskalasyonlar + son 500 kapalı kayıt yüklenir.  
**Yazma:** Write-through.  
**Durum geçişleri:** Cache'e `EscalationStateFactory` uygulanır, ardından DB UPDATE.  
**Redis:** `csbot:escalation:created` / `csbot:escalation:decided`.  
**Agent kapsamlı geçmiş:** `GetRecentForAgentAsync` cache'i DEĞİL, doğrudan veritabanını sorgular
(`WHERE assigned_to IS NULL OR assigned_to = @agentId ... LIMIT`). Sebep: cache'in kendisi bir
"son N" penceresidir; bir agent'ın kapalı kaydı o pencerenin gerisinde kalabilir ve cache üzerinde
filtrelemek onu görünmez bırakır. Bu, sınırı ötelemekle çözülmez — daraltma ve limit birlikte,
veri kaynağında uygulanmalıdır.

---

## PostgresHumanAgentRegistry

**Port:** `IHumanAgentRegistry`

**Hydration:** Tüm tablo lazy full-hydration.  
**IncrementLoad/DecrementLoad:** Per-agent `SemaphoreSlim(1,1)` + DB UPDATE.  
**GetLinkedUsersAsync:** `auth.users WHERE role='Agent' AND linked_agent_id IS NOT NULL` sorgular.

---

## PostgresRatingStore

**Port:** `IRatingStore`

**Yazma stratejisi:** Durable-first — UPSERT `analytics.ratings` önce, cache sonra.  
**Hydration:** Tüm tablo lazy full-hydration.

---

## PostgresReasoningTraceStore

**Port:** `IReasoningTraceStore`

**Üç aşamalı strateji:**

| Metod | Cache | DB |
| ------- | ------- | ----- |
| `StartTrace` | Ekle | INSERT skeleton (minimal alanlar) |
| `Update` | Güncelle | **Hayır** — hot path'de DB yazılmaz |
| `Complete` | Güncelle | UPDATE tam içerik |

Cache miss durumunda `Get(traceId)` DB fallback kullanır.  
Startup: `PersistenceHydrator` tamamlanmamış trace'leri `terminated_by_restart` yapar.

---

## PostgresSlaEventSink

**Port:** `ISlaEventSink`

**Yazma:** In-memory enqueue + async DB INSERT (hata swallow — monitoring amacıyla).  
**Hydration:** Tüm tablo lazy full-hydration.  
**`LastEmittedAt`:** Cache'te aynı `(kind, targetId, severity)` için son emit zamanı — duplicate event önleme.

---

## PostgresLessonStore

**Port:** `ILessonStore`

**Hydration:** Tüm tablo lazy.  
**Yazma:** UPSERT `improvement.lessons`.

---

## PostgresCustomerProfileStore

**Port:** `ICustomerProfileStore`

**Hydration:** Tüm tablo lazy.  
**Yazma:** Write-through — cache güncelle + UPSERT `personalization.customer_profiles`.  
**JSONB:** `IntentFrequencyJson`, `ProductInterestsJson`, `RecentRatingsJson` serialize/deserialize edilir.

---

## PostgresLlmCallUsageSink

**Port:** `ILlmCallPersistencePort`

Fire-and-forget LLM çağrı kaydı. `IDbContextFactory` ile her insert için kısa ömürlü DbContext.

```csharp
await db.LlmCallUsages.AddAsync(new LlmCallUsageEntity
{
    Model = record.Model,
    Provider = record.Provider,
    InputTokens = record.InputTokens,
    OutputTokens = record.OutputTokens,
    CostUsd = record.CostUsd,
    DurationMs = record.DurationMs,
    CalledAt = record.CalledAt
});
await db.SaveChangesAsync();
```

Exception swallow: logging yapar, exception'ı yutar — telemetry kaybı kabul edilebilir.

**InMemory karşılığı yoktur** — `ILlmCallPersistencePort` sadece Postgres modunda anlamlıdır.

---

## OrderRepository

**Port:** `IOrderRepository`

EF Core `IDbContextFactory` ile her çağrıda kısa ömürlü `DbContext` yaratır.

| Metod | Açıklama |
| ------- | --------- |
| `PlaceOrder(order)` | **Stok düşümü + sipariş yazımı, tek transaction'da** — sipariş vermenin tek doğru yolu |
| `Create(order)` | Yeni sipariş + **tüm** satırların INSERT'i; `Code` DB tarafından üretilir (stoğa dokunmaz) |
| `Get(orderId)` | Tekil sipariş (Include: Details + Product) |
| `GetByCustomer(customerId)` | Müşteriye ait tüm siparişler (OrderDate DESC) |
| `GetLast(customerId)` | Son sipariş |
| `Cancel(orderId, reason)` | İptal — sadece `Processing`/`Shipped` durumunda |
| `RequestReturn(orderId, reason)` | İade talebi — sadece `Delivered` + 14 gün süresi |

### Çok satırlı sipariş

`catalog.order_details` tablosu sipariş başına N kayıt tutar; birincil anahtarı `(order_code, product_id)`'dir. Şema **en baştan beri** çok satırlıydı ama `MapToModel` uzun süre `Details.FirstOrDefault()` çağırdığı için ikinci ve sonraki ürünler sessizce kayboluyordu. Artık:

- `MapToModel` tüm satırları okur ve `OrderInfo.Lines`'a doldurur (ürün adına göre sıralı — okuma deterministik olsun diye).
- `Create` satırların hepsini yazar. Başlık ve satırlar iki ayrı `SaveChanges` gerektirir (sipariş kodu DB tarafından üretilir ve satırların FK'sı odur), bu yüzden **ikisi tek transaction'a alınır** — aksi hâlde araya düşen bir hata satırsız bir "hayalet sipariş" bırakırdı. Transaction, aşağıdaki retry kuralına uyar.
- `Create`, satırsız bir `OrderInfo` gelirse `ArgumentException` fırlatır.

### Sipariş vermek neden `PlaceOrder`?

Sipariş vermek iki yazma içerir: ürün stoğunun düşülmesi ve siparişin yazılması. Bunlar bir süre ayrı transaction'lardaydı (`IProductCatalogRepository.TryDeductStock` + `Create`) ve aradaki herhangi bir hata — DB kesintisi, retry tükenmesi, pod'un ölmesi — stoğu düşülmüş ama karşılığında hiçbir sipariş oluşmamış hâlde bırakıyordu. Arıza sessizdi: kullanıcı hata alıp tekrar dener, stok bir daha geri gelmez; yeterince tekrarlandığında ürün, deposunda dururken "stokta yok" hâline gelir.

`PlaceOrder` ikisini tek transaction'da yapar. Dönüş tipi `OrderPlacementResult`, "stok düşüldü ama sipariş yok" ara durumunu **temsil edemeyecek** şekilde tasarlanmıştır: `OrderId` doluysa ikisi de olmuştur, `Stock` başarısızsa hiçbiri.

`TryDeductStock` bu yüzden **kaldırıldı** — tek başına stok düşmek, sipariş akışında her zaman yanlıştı.

---

## ComplaintRepository

**Port:** `IComplaintRepository`

| Metod | Açıklama |
| ------- | --------- |
| `Create(complaint)` | Yeni şikayet; `catalog.complaint_seq` sequence'tan ID alır |
| `Get(complaintId)` | Tekil şikayet |
| `GetByOrder(orderId)` | Siparişe ait şikayetler |
| `GetByCustomer(customerId)` | Müşteriye ait şikayetler |

> **Not:** `Create` metodu `catalog.complaint_seq` sequence kullanır. Bu sequence migration'da tanımlı olmalıdır.

---

## ProductCatalogRepository

**Port:** `IProductCatalogRepository`

| Metod | Açıklama |
| ------- | --------- |
| `FindProduct(name)` | İsme göre ürün arama (exact match) |
| `GetAll()` | Tüm ürünler (kategori dahil, Name sıralı) |
| `GetByCategory(category)` | Kategoriye göre ürünler — **[`CategoryProducts`](../CustomerSupportBot.Domain/Model/CategoryProducts.md)** döner |
| `GetSelectableCategories()` | **Yalnızca en az bir ürünü olan** kategori isimleri |

**Kategori sorgularında iki incelik:**

`GetByCategory` boş liste yerine `CategoryProducts` döner, çünkü "böyle bir kategori yok" ile
"kategori var ama içi boş" çağıran için zıt anlamlar taşır (ayrıntı ve düzeltilen hata için
bkz. [`CategoryProducts.md`](../CustomerSupportBot.Domain/Model/CategoryProducts.md)).
Dönen `CanonicalName` katalogdaki yazımdır — kolon `und-u-ks-level1` collation'lı olduğu için
eşleşme farklı yazımlarla da gerçekleşir.

`GetSelectableCategories` (eski adı `GetCategories`) boş kategorileri **kasıtlı olarak**
eler: bu liste kategori seçim ekranını besler ve içi boş bir kategoriyi seçenek olarak
göstermek kullanıcıyı çıkmaz sokağa sokar. Seed'de 4 kategori gerçekten boştur.

**Stok düşürme — ya hep ya hiç:**

Her satır `ExecuteUpdate` ile tek koşullu SQL olarak düşülür (`WHERE name = ? AND stock >= qty`); bu koşul lock gerektirmeden race condition'ı önler. Satırların tamamı **tek transaction** içindedir: biri bile 0 satır etkilerse (stok yetmiyor) transaction rollback edilir ve o ana kadar düşülenler geri alınır.

Neden gerekli: satırlar bağımsız düşülseydi, üçüncü satır yetmediğinde ilk ikisinin stoğu düşmüş ama sipariş oluşmamış olurdu — stok sessizce kaybolurdu.

Başarısızlıkta `StockDeductionResult.Shortages` yetersiz kalan satırları taşır (`ürün`, `istenen`, `mevcut`) — kullanıcıya "hangi üründen kaç adet var" diyebilmek için.

> ⚠️ Satırlar **ürün adına göre sıralı** işlenir. Bu kozmetik değil: iki eşzamanlı sipariş aynı iki ürünü ters sırada kilitlerse Postgres deadlock verir. Sabit sıra kilitleme düzenini deterministik yapar.

---

## ⚠️ Transaction açacaksanız: retry stratejisi kuralı

`AddCustomerSupportPersistence` `EnableRetryOnFailure(maxRetryCount: 3)` ile kayıt yapar, yani üretimdeki strateji `NpgsqlRetryingExecutionStrategy`'dir. Bu strateji **elle açılan (user-initiated) transaction'ları reddeder**:

```
System.InvalidOperationException: The configured execution strategy
'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions.
```

Sebep mantıklı: geçici bir hatada yalnızca tek bir komutu yeniden denemek, çok komutlu bir transaction'ı yarıda bırakabilir. Bu yüzden transaction'ın **tamamı** stratejiye tek bir yeniden-denenebilir birim olarak verilmelidir:

```csharp
using var probe = _dbFactory.CreateDbContext();
var strategy = probe.Database.CreateExecutionStrategy();

return strategy.Execute(() =>
{
    using var ctx = _dbFactory.CreateDbContext();   // ← delegate'in İÇİNDE
    using var tx = ctx.Database.BeginTransaction();
    // ...
    tx.Commit();
});
```

`DbContext`'in delegate'in **içinde** açılması şart: yeniden denemede taze bir change-tracker gerekir, yoksa ilk denemede eklenmiş entity'ler ikinci denemede tekrar yazılır.

> 🐞 **Bu kural bir canlı hatasıyla öğrenildi.** Çok satırlı sipariş/stok transaction'ları testlerde geçip üretimde patladı; onay verilmiş bir sipariş `[HITL] Approval execution başarısız` ile düştü. Sebep test tarafındaydı: `PostgresCatalogFixture` `EnableRetryOnFailure` çağırmıyordu, dolayısıyla testlerde strateji elle transaction'a izin veren varsayılan `ExecutionStrategy` oluyordu. Fixture artık üretimle aynı yapılandırmayı kullanıyor — **testler ile üretim arasındaki DbContext yapılandırma farkı, bu sınıftaki hataların saklandığı yerdir.**

---

## CustomerRepository

**Port:** `ICustomerRepository`

| Metod | Açıklama |
|-------|---------|
| `Exists(customerId)` | Müşteri var mı kontrolü |

Minimal adapter — müşteri doğrulama için kullanılır.
