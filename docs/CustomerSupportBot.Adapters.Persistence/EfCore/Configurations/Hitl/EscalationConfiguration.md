# EscalationConfiguration

**Dosya:** `EfCore/Configurations/Hitl/EscalationConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<EscalationEntity>`
**Entity:** [EscalationEntity](../../Entities/Hitl/EscalationEntity.md)

## 1. Ne İşe Yarar

`EscalationEntity`'nin `hitl.escalations` tablosuna eşlemesini ve iki index'ini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `(Status, CreatedAt)` bileşik index; `(SessionId, AgentName)` üzerinde
**unique + filtreli** (yalnızca açık kayıtlar) index — session + ajan başına tek açık eskalasyon.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — `SessionId`/`AssignedTo` gerçek FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ux_escalations_open_session_agent` (`OpenDedupIndexName`) — `(session_id, agent_name)` üzerinde
UNIQUE, `WHERE status IN ('Open','Acknowledged')` filtreli. Dedup'ın asıl garantisidir:
[EscalationPolicyService](../../../../CustomerSupportBot.Application/Services/Escalation/EscalationPolicyService.md)'in
"önce açık kayıt var mı bak, sonra ekle" adımı atomik değildi; bileşik sorgunun paralel alt
görevleri ya da farklı pod'lar aynı ajan için iki eskalasyon açabiliyordu. Kısıt yarışın
kazananını DB'de belirler; kaybeden mevcut kaydı alır (bkz.
[PostgresEscalationSink](../../../Postgres/PostgresEscalationSink.md)). Aynı index açık
eskalasyonları session'a göre bulma sorgusunu da karşılar.

`NULL` session/ajan değerleri birbirini engellemez (Postgres'te `NULL`'lar farklı sayılır) —
session'sız kayıtlar dedup'a girmez.

> **Migration notu (`AddEscalationOpenDedup`):** Unique index, kısıt yokken birikmiş mükerrer
> açık kayıtlar varsa oluşturulamaz. Migration önce aynı session + ajan için **en eski** açık
> kaydı bırakıp diğerlerini `Dismissed` olarak kapatır (silmez — denetim izi kalır, `resolution`
> alanına gerekçe yazılır), sonra index'i oluşturur.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `const string OpenDedupIndexName` | `"ux_escalations_open_session_agent"` — kısıt ihlali yakalanırken bu adla eşleştirilir. |
| `Configure(EntityTypeBuilder<EscalationEntity>)` | `Id` PK; `UserQuery`/`Reason`/`MissingContextJson`(`jsonb`)/`CreatedAt`/`Status` zorunlu; diğerleri opsiyonel; 2 index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [EscalationEntity](../../Entities/Hitl/EscalationEntity.md)
- [README](../README.md)
