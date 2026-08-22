# ApprovalDecisionInput

**Dosya:** `Ports/Inbound/ApprovalDecisionInput.cs`
**Tür:** `class`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Admin panelinin bir HITL (human-in-the-loop) onay isteğini onaylarken/reddederken gönderdiği HTTP request body'sinin tipli karşılığı. Sadece veri taşır, davranış içermez.

## 2. Hangi amaçla kullanılır?

`POST /admin/approvals/{id}/decide` gibi bir admin endpoint'i, bu tipi request body olarak deserialize eder ve `IApprovalPort.DecideAsync(id, Approved, DecidedBy, Reason)` çağrısına aktarır.

## 3. Sorumlulukları

- **Üstlendiği:** Onay kararının üç alanını (`Approved`/`DecidedBy`/`Reason`) taşımak.
- **Üstlenmediği:** Kararın iş kurallarını uygulamak (bu iş `IApprovalPort`/`IApprovalQueue` içindedir); bu tip sadece bir DTO'dur.

## 4. Diğer katman/bileşenlerle ilişkileri

- Api katmanındaki admin approval endpoint'i tarafından request body olarak kullanılır.
- `IApprovalPort.DecideAsync` çağrısının parametrelerini paketler.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Hexagonal mimaride, Api katmanının Application katmanına geçtiği "sınır"da (use case boundary) tipli bir DTO kullanmak, sözleşmeyi netleştirir ve `IApprovalPort.DecideAsync`'in imzasını Api tarafına sızdırmadan (ayrı parametreler yerine tek nesne) değiştirilebilir kılar.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Approved` | `bool` | `true` → onaylandı, `false` → reddedildi. |
| `DecidedBy` | `string?` | Kararı veren kişi/kullanıcı adı; boşsa çağıran taraf varsayılan bir değer (`"admin"`) kullanabilir. |
| `Reason` | `string?` | Red gerekçesi — reddedildiğinde kullanıcıya gösterilir. |

## 7. Bağımlılıklar

Yok — saf bir DTO, hiçbir servise bağımlı değil.

## Bağlantılar

- [IApprovalPort](IApprovalPort.md) — bu DTO'yu tüketen port.
