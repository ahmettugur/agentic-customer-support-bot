# EscalationEntity

**Dosya:** `EfCore/Entities/Hitl/EscalationEntity.cs`
**Şema/Tablo:** `hitl.escalations`
**Configuration:** [EscalationConfiguration](../../Configurations/Hitl/EscalationConfiguration.md)

## 1. Ne İşe Yarar

Reasoning katmanının bir turda `needs_escalation` durumu üretmesi sonucu oluşan, bir insan
temsilciye devredilmesi gereken konuşmanın kaydını temsil eder.

## 2. Hangi Amaçla Kullanılır

`HitlEventPortService`/eskalasyon akışı bu kaydı oluşturur; admin panelinde "bekleyen
eskalasyonlar" listesi buradan beslenir; bir temsilci kaydı sahiplendiğinde
(`AssignedTo`) ve çözdüğünde (`ResolvedAt`/`Resolution`) güncellenir.

## 3. Sorumlulukları

- **Üstlendiği:** Neden eskalasyona ihtiyaç duyulduğunu (`Reason`, `MissingContextJson`),
  durumunu ve çözüm bilgisini taşımak.
- **Üstlenmediği:** Eskalasyon kararının kendisi (reasoning'in `needs_escalation` üretmesi) —
  bu Domain/Application katmanının işi, entity sadece sonucu kaydeder.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId` üzerinden [SessionEntity](../Chat/SessionEntity.md)'ye mantıksal referans verir.
`AssignedTo` alanı `HumanAgentEntity.Id`'ye mantıksal referans verir (bkz.
[HumanAgentEntity](HumanAgentEntity.md)).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`ux_escalations_open_session_agent` neden unique VE filtreli:** Aynı oturumda aynı
> ajandan ikinci bir açık eskalasyon oluşmasını DB seviyesinde engeller. Eskiden burada yalnızca
> `session_id` üzerinde UNIQUE OLMAYAN bir index vardı (`ix_escalations_session_open`) ve dedup
> uygulama tarafındaki atomik olmayan bir "önce kontrol, sonra ekle" adımına dayanıyordu. Filtre
> (`Open`/`Acknowledged`) çözülmüş/reddedilmiş eski kayıtları kapsam dışı bırakır — aynı oturumda
> önceki eskalasyon kapandıktan sonra yenisi açılabilir. Bkz.
> [EscalationConfiguration](../../Configurations/Hitl/EscalationConfiguration.md).

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Birincil anahtar. |
| `SessionId` | `string?` | İlgili oturum. |
| `TraceId` | `string?` | Reasoning trace ile ilişki. |
| `AgentName` | `string?` | Eskalasyonu tetikleyen uzman ajan. |
| `UserQuery` | `string` | Kullanıcının talebi. |
| `Reason` | `string` | Eskalasyon nedeni. |
| `MissingContextJson` | `string` | Eksik bağlam listesi, `jsonb`. |
| `ResponseSummary` | `string?` | Bota kadarki özet. |
| `CreatedAt` | `DateTime` | Oluşturulma zamanı. |
| `AcknowledgedAt` | `DateTime?` | Bir temsilcinin sahiplendiği zaman. |
| `ResolvedAt` | `DateTime?` | Çözüldüğü zaman. |
| `Status` | `string` | `"Open"` (varsayılan) \| `"Acknowledged"` \| `"Resolved"` \| `"Dismissed"`. |
| `AssignedTo` | `string?` | Sahiplenen temsilci kimliği. |
| `Resolution` | `string?` | Çözüm açıklaması. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [EscalationConfiguration](../../Configurations/Hitl/EscalationConfiguration.md)
- [HumanAgentEntity](HumanAgentEntity.md), [SessionEntity](../Chat/SessionEntity.md)
- [README](../README.md)
