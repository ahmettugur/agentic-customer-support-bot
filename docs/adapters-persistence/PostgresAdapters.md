# Postgres Adaptörler

`Postgres/` altındaki tüm adaptörler **Hybrid Cache + DB** deseni uygular. Her biri InMemory karşılığı ile aynı port'u implement eder; ek olarak PostgreSQL kalıcılığı ve opsiyonel Redis pub/sub ekler.

Temel desen için önce [HybridPattern.md](HybridPattern.md) oku.

---

## PostgresSessionManager

**Port:** `ISessionManager`

**Yazma:** `AddExchange` → cache + DB INSERT (2 satır: user + assistant)  
**Hydration:** `GetAll()` çağrısında son 500 session metadata yüklenir. Tekil session `GetOrCreate`'te lazy yüklenir.

**`AppendAssistantMessage` özelliği:** Son mesaj boş asistan mesajı ise onu replace eder — streaming sırasında placeholder yazılmış olabilir.

**`ExtractAndUpdateState`:** In-memory `SessionStateExtractor` ile kurallar uygulanır, ardından `chat.sessions.state` JSONB güncellenir.

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
|-------|-------|-----|
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

## PostgresWorkflowDefinitionStore

**Port:** `IWorkflowDefinitionStore`

**Hydration:** Tüm tablo lazy.  
**Upsert:** `version` otomatik artırılır; ID yoksa `Slugify(name)` ile türetilir.  
**Slugify:** `ç→c`, `ğ→g`, `ı→i`, `ö→o`, `ş→s`, `ü→u` + küçük harf + alfanümerik filtre.

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
