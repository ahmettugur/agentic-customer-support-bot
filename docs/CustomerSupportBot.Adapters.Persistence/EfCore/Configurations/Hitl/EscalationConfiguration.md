# EscalationConfiguration

**Dosya:** `EfCore/Configurations/Hitl/EscalationConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<EscalationEntity>`
**Entity:** [EscalationEntity](../../Entities/Hitl/EscalationEntity.md)

## 1. Ne İşe Yarar

`EscalationEntity`'nin `hitl.escalations` tablosuna eşlemesini ve iki index'ini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `(Status, CreatedAt)` bileşik index; `SessionId` üzerinde **filtreli**
(sadece açık kayıtlar) index.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — `SessionId`/`AssignedTo` gerçek FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ix_escalations_session_open` filtreli index'i (`WHERE status IN ('Open','Acknowledged')`) —
bkz. [EscalationEntity](../../Entities/Hitl/EscalationEntity.md) 🐞 notu: aynı oturumda ikinci
bir açık eskalasyon oluşmasını engellemek (dedup) için sık çalışan bir sorguyu hızlandırır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<EscalationEntity>)` | `Id` PK; `UserQuery`/`Reason`/`MissingContextJson`(`jsonb`)/`CreatedAt`/`Status` zorunlu; diğerleri opsiyonel; 2 index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [EscalationEntity](../../Entities/Hitl/EscalationEntity.md)
- [README](../README.md)
