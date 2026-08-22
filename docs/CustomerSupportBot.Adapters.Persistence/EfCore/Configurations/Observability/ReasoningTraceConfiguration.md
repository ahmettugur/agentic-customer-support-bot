# ReasoningTraceConfiguration

**Dosya:** `EfCore/Configurations/Observability/ReasoningTraceConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<ReasoningTraceEntity>`
**Entity:** [ReasoningTraceEntity](../../Entities/Observability/ReasoningTraceEntity.md)

## 1. Ne İşe Yarar

`ReasoningTraceEntity`'nin `observability.reasoning_traces` tablosuna eşlemesini tanımlar —
katmandaki en çok alanlı Configuration dosyalarından biridir (trace'in tüm alt-JSON
alanlarını `jsonb` kolonlara eşler).

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri (çoğu `jsonb`); `(SessionId, StartedAt)` bileşik index; `StartedAt` üzerinde
azalan index.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — `SessionId` gerçek FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ix_traces_session_started` — "bu oturumun trace geçmişini kronolojik sırayla getir" (bir
oturumdaki tüm turların akıl yürütme kayıtları) sorgusunu destekler. `ix_traces_started`
(azalan) — genel "en son trace'ler" listesi (debug/izleme ekranı) içindir. Çoğu alanın nullable
olması (`ReasoningJson`, `PlanningJson`, `FinalCritiqueJson` vb.), bir turun her aşamasının
her zaman çalışmayabileceğini yansıtır (ör. reasoning aşaması hata verirse planning hiç
üretilmez).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<ReasoningTraceEntity>)` | `TraceId` PK; `SessionId`/`UserQuery`/`StartedAt` zorunlu; çoğu alan (`ReasoningJson`, `PlanningJson`, `FinalCritiqueJson` vb.) opsiyonel `jsonb`; `SpecialistReasoningsJson`/`AgentVisitsJson`/`ToolCallsJson` zorunlu `jsonb` diziler; 2 index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [ReasoningTraceEntity](../../Entities/Observability/ReasoningTraceEntity.md)
- [README](../README.md)
