# SlaEventEntity

**Dosya:** `EfCore/Entities/Analytics/SlaEventEntity.cs`
**Şema/Tablo:** `analytics.sla_events`
**Configuration:** [SlaEventConfiguration](../../Configurations/Analytics/SlaEventConfiguration.md)

## 1. Ne İşe Yarar

`SlaGuardianService` (Api katmanı, arka plan servisi) tarafından üretilen SLA uyarı/ihlal
olaylarının kalıcı kaydını temsil eder (ör. "bu escalation 30 dakikadır cevaplanmadı").

## 2. Hangi Amaçla Kullanılır

SLA takibi için periyodik olarak taranan hedeflerin (bekleyen escalation, onay talebi vb.)
eşik aşımlarını kaydeder; admin panelinde SLA ihlali raporlarında gösterilir.

## 3. Sorumlulukları

- **Üstlendiği:** Bir SLA olayının türünü (`Kind`), önem derecesini (`Severity`), hangi hedefe
  ait olduğunu (`TargetId`) ve o an ne kadar "yaşlı" olduğunu (`AgeSeconds`) taşımak.
- **Üstlenmediği:** SLA eşiklerinin hesaplanması/taranması — bu `SlaGuardianService`'in işi,
  entity sadece sonucu kaydeder.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`TargetId`, bağlama göre bir `EscalationEntity.Id` veya `ApprovalRequestEntity.Id` gibi başka
bir kaydı gösterebilir — polymorphic bir referanstır, gerçek FK yoktur (hedef tipi sabit değil).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`(Kind, TargetId, Severity)` bileşik index'i, "belirli bir hedef için belirli türde bir olay
daha önce kaydedildi mi" sorgusunu hızlandırır — `SlaGuardianService`'in aynı ihlali tekrar
tekrar kaydetmemesi (dedup) için tipik kullanım şeklidir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Birincil anahtar. |
| `Timestamp` | `DateTime` | Olayın oluştuğu zaman. |
| `Kind` | `string` | Olay türü (ör. "escalation_stale"). |
| `Severity` | `string` | Önem derecesi (ör. "warn", "breach"). |
| `TargetId` | `string` | Hangi kayda ait (polymorphic referans). |
| `AgeSeconds` | `int` | Hedefin o anki "yaşı" (saniye). |
| `Action` | `string?` | Otomatik alınan aksiyon (varsa). |
| `Note` | `string?` | Serbest metin not. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [SlaEventConfiguration](../../Configurations/Analytics/SlaEventConfiguration.md)
- [README](../README.md)
