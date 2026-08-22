# SlaEventConfiguration

**Dosya:** `EfCore/Configurations/Analytics/SlaEventConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<SlaEventEntity>`
**Entity:** [SlaEventEntity](../../Entities/Analytics/SlaEventEntity.md)

## 1. Ne İşe Yarar

`SlaEventEntity`'nin `analytics.sla_events` tablosuna eşlemesini ve iki index'ini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `Timestamp` üzerinde azalan index; `(Kind, TargetId, Severity)` bileşik
index (dedup/sorgu amaçlı).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız bir tablo — `TargetId` polymorphic bir referans olduğu için gerçek FK tanımlanamaz.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

İki index'in amacı farklıdır: `ix_sla_events_timestamp` (azalan) "en son olaylar" listesi
içindir; `ix_sla_events_kind_target_severity` ise "bu hedef için bu tür+önem derecesinde olay
var mı" dedup sorgusu içindir — bkz. [SlaEventEntity](../../Entities/Analytics/SlaEventEntity.md).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<SlaEventEntity>)` | `Id` PK; `Timestamp`/`Kind`/`Severity`/`TargetId`/`AgeSeconds` zorunlu; `Action`/`Note` opsiyonel; iki index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [SlaEventEntity](../../Entities/Analytics/SlaEventEntity.md)
- [README](../README.md)
