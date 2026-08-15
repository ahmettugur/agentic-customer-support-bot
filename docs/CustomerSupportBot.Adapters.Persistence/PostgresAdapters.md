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

---

## PostgresApprovalQueue

**Port:** `IApprovalQueue`

**Hydration:** Son 200 kayıt lazy yüklenir.  
**Yazma stratejisi:** Durable-first — DB INSERT/UPDATE önce, cache sonra.  
**Dağıtık lock:** `Decide()` çağrısında `IAppDistributedLock.AcquireAsync("approval:decide:{id}")` — birden fazla pod aynı anda karar veremez.  
**Redis:** `csbot:approval:created` / `csbot:approval:decided` kanalları.  
**Startup:** `ExpirePendingOnStartupAsync()` — `PersistenceHydrator` çağırır.

---

## PostgresChatBridge

**Port:** `IChatBridge`

**Hydration:** Per-session lazy (session ilk abonelikte yüklenir).  
**Yazma:** BotTyping hariç tüm mesajlar DB'ye yazılır (audit trail).  
**Redis:** `csbot:bridge:touser` / `csbot:bridge:toadmin` — multi-pod live chat.  
**Reset:** In-memory geçmişi temizler; DB kayıtlarına dokunmaz (audit korunur).

---

## PostgresChatModeRegistry

**Port:** `IChatModeRegistry`

**Hydration:** Tüm tablo lazy full-hydration.  
**Yazma:** UPSERT `chat.session_modes`.  
**Dağıtık lock:** `TakeOver()` sırasında `IAppDistributedLock.AcquireAsync("chatmode:{sessionId}")` — eş zamanlı iki TakeOver önlenir.  
**Redis:** `csbot:chatmode` kanalı — mod değişikliği broadcast.

---

## PostgresEscalationSink

**Port:** `IEscalationSink`

**Hydration:** Açık eskalasyonlar + son 500 yüklenir.  
**Yazma:** Write-through.  
**Durum geçişleri:** Cache'e `EscalationStateFactory` uygulanır, ardından DB UPDATE.  
**Redis:** `csbot:escalation:created` / `csbot:escalation:decided`.

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
| `Create(order)` | Yeni sipariş + **tüm** satırların INSERT'i; `Code` DB tarafından üretilir |
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
| `TryDeductStock(lines)` | Bir siparişin **tüm** satırlarının stoğunu tek transaction'da düşer |
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
