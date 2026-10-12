# PostgresPerformanceStore (`IPerformanceStore`)

**Dosya:** `Postgres/PostgresPerformanceStore.cs`

Performans ekranının toplama sorguları; hesap veritabanında yapılır, uygulamaya yalnızca gruplanmış satırlar gelir.
Aralık `[from, to)`.

| Sorgu | Kaynak |
|---|---|
| Toplamlar | `observability.reasoning_traces` (`started_at`): tur, yanıtlanan (`termination_reason ∈ {completed, awaiting_user_input}` ve hata yok), hatalı, ortalama süre, `escalation_needed`. |
| Ajan turları | `agent_visits` jsonb dizisi — `split_part(AgentName,'_',1)` ile yürütücü soneki ("OrderAgent_35e6…") atılır; `DurationMs` ortalaması. |
| Görev sonuçları | `specialist_reasonings[].PostToolReflection.Status`. |
| Araçlar | `tool_calls[]` — çağrı, `Success = false` sayısı, en son `ResultSummary`. |
| Bitiş nedenleri | `termination_reason` (boşsa `unknown`). |
| Temsilciler | `hitl.escalations` (atanan, üstlenme/çözüm süreleri), `catalog.complaint_events` (`resolved`/`rejected`, atanan temsilci), `analytics.ratings` — puan **oturum başına bir kez** sayılır (aynı oturumda birden çok eskalasyon olabilir). |

Trace JSON'u PascalCase serileştirilir (`PostgresReasoningTraceStore`). jsonb dizisi olmayan değerler `[]` sayılır.
Satır tipleri `SqlQuery<T>` ile sütun adından eşlenen ayarlanabilir özellikli sınıflardır.

Testler (gerçek Postgres): `PostgresPerformanceStoreTests`.
