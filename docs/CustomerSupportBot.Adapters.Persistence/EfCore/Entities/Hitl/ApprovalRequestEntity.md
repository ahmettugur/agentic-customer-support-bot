# ApprovalRequestEntity

**Dosya:** `EfCore/Entities/Hitl/ApprovalRequestEntity.cs`
**Şema/Tablo:** `hitl.approval_requests`
**Configuration:** [ApprovalRequestConfiguration](../../Configurations/Hitl/ApprovalRequestConfiguration.md)

## 1. Ne İşe Yarar

Sistemin HITL (human-in-the-loop) onay mekanizmasının kalbi olan varlıktır — admin onayı
gerektiren bir tool çağrısının (sipariş verme, şikayet kaydı, sipariş iptali, iade) tüm yaşam
döngüsünü (talep → karar → yürütme → müşteri bildirimi) tek satırda taşır.

## 2. Hangi Amaçla Kullanılır

`ApprovalGateService` bu kaydı oluşturur (`CreateAsync`); admin panelindeki onay/red işlemi
`DecidedAt`/`Status`/`DecidedBy`/`DecisionReason` alanlarını doldurur; onaylanırsa
`IApprovalExecutionRouter` gerçek işi yürütüp sonucu `ExecutionResult`/`ExecutionStatus`'a
yazar; müşteri bildirimi görünce `CustomerSeenAt` doldurulur.

## 3. Sorumlulukları

- **Üstlendiği:** Bir onay talebinin TÜM durumunu (talep, karar, yürütme, görülme) tek satırda
  saklamak — Postgres+Redis ile çoklu pod'da tutarlı, kalıcı bir kayıt.
- **Üstlenmediği:** `TaskCompletionSource` (senkron bekleyen HTTP isteğini tamamlatan .NET
  nesnesi) — bu asla persist edilmez, sadece in-memory'de tutulur (kod yorumunda açıkça
  belirtilmiş); bir pod yeniden başlarsa bekleyen `TaskCompletionSource`'lar kaybolur ama
  DB kaydı kalır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId`/`CustomerId` üzerinden [SessionEntity](../Chat/SessionEntity.md) ve
`CustomerEntity`'ye mantıksal referans verir (gerçek FK yok). `ApprovalGateService`,
`PostgresApprovalQueue`/`InMemoryApprovalQueue`, `IApprovalExecutionRouter`,
`StaleApprovalSweepService` bu tabloyu okur/yazar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ParametersJson`/`MissingContextJson` gibi alanlar `jsonb` olarak saklanır — tool parametreleri
her tool için farklı şekilde olduğundan (sipariş verme parametreleri ≠ iade parametreleri)
sabit kolonlar yerine esnek bir JSON blob tercih edilmiştir.

> 🐞 **`ExecutionResult`/`ExecutionStatus`/`ExecutedAt` neden ayrı alanlar:** Onaylanma
> (`Status=Approved`) ile gerçek işin başarıyla tamamlanması (`ExecutionStatus=Succeeded`) iki
> farklı olaydır — admin onaylasa bile gerçek iş (stok yetersiz, DB hatası vb.) başarısız
> olabilir. Bu ayrım, "Approved + Running/Failed" gibi askıda kalmış durumları ayrı ayrı
> sorgulanabilir kılar (bkz. `ix_approvals_status_execution_status` index'i).
>
> **`CustomerSeenAt` neden var:** Müşteri chat sayfasını kapatıp admin onayladıktan sonra
> tekrar açtığında, hangi kararların "yeni/görülmemiş" olduğunu göstermek için — `null` ise
> unseen, bir bildirim/badge sistemi bu alanı okur.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Birincil anahtar. |
| `SessionId` | `string?` | Talebin geldiği oturum. |
| `CustomerId` | `string?` | Talebi tetikleyen müşteri (JWT'den, LLM'den değil). |
| `TraceId` | `string?` | Reasoning trace ile ilişki. |
| `ToolName` | `string` | Hangi tool onay bekliyor (ör. `order_cancel`). |
| `AgentName` | `string?` | Talebi tetikleyen uzman ajan. |
| `ParametersJson` | `string` | Tool parametreleri, `jsonb`. |
| `UserQuery` | `string?` | Kullanıcının orijinal talebi. |
| `Justification` | `string?` | LLM'in onay gerekçesi. |
| `RequestedAt` | `DateTime` | Talep zamanı. |
| `DecidedAt` | `DateTime?` | Karar zamanı. |
| `Status` | `string` | `"Pending"` (varsayılan) \| `"Approved"` \| `"Rejected"` \| `"Expired"`. |
| `DecidedBy` | `string?` | Kararı veren admin/sistem. |
| `DecisionReason` | `string?` | Red/onay gerekçesi. |
| `TimeoutSeconds` | `int` | Otomatik red için zaman aşımı eşiği. |
| `ExecutionResult` | `string?` | Gerçek işin sonucu (onaylandıysa). |
| `ExecutedAt` | `DateTime?` | Yürütme zamanı. |
| `ExecutionStatus` | `string` | `"None"` (varsayılan) \| `"Running"` \| `"Succeeded"` \| `"Failed"`. |
| `CustomerSeenAt` | `DateTime?` | Müşterinin bu kararı gördüğü zaman, `null` → unseen. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [ApprovalRequestConfiguration](../../Configurations/Hitl/ApprovalRequestConfiguration.md)
- [SessionEntity](../Chat/SessionEntity.md)
- [README](../README.md)
