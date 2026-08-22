# ApprovalRequestConfiguration

**Dosya:** `EfCore/Configurations/Hitl/ApprovalRequestConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<ApprovalRequestEntity>`
**Entity:** [ApprovalRequestEntity](../../Entities/Hitl/ApprovalRequestEntity.md)

## 1. Ne İşe Yarar

`ApprovalRequestEntity`'nin `hitl.approval_requests` tablosuna eşlemesini ve 4 index'ini
tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri (`ParametersJson` → `jsonb`); admin panelindeki farklı sorgu paternlerini
destekleyen 4 ayrı index.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — `SessionId`/`CustomerId` gerçek FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Dört index'in her biri farklı bir sorgu senaryosuna hizmet eder:
- `ix_approvals_status_requested_at` — "bekleyen talepleri tarihe göre sırala" (admin kuyruğu).
- `ix_approvals_session_id` — "bu oturumun onay geçmişi".
- `ix_approvals_customer_id` — "bu müşterinin onay/bildirim geçmişi" (unseen-badge sorgusu).
- `ix_approvals_status_execution_status` — "onaylandı ama yürütme takıldı/başarısız oldu"
  (askıda kalmış işleri bulmak için, bkz. [ApprovalRequestEntity](../../Entities/Hitl/ApprovalRequestEntity.md)
  🐞 notu).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<ApprovalRequestEntity>)` | `Id` PK; `ToolName`/`ParametersJson`(`jsonb`)/`RequestedAt`/`Status`/`TimeoutSeconds`/`ExecutionStatus` zorunlu; diğerleri opsiyonel; 4 index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [ApprovalRequestEntity](../../Entities/Hitl/ApprovalRequestEntity.md)
- [README](../README.md)
