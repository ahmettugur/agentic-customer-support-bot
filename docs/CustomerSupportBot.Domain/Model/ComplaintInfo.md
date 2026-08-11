# ComplaintInfo

**Dosya:** `Model/ComplaintInfo.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

Bir şikayetin domain modelidir — sipariş ID, müşteri, şikayet metni ve durum bilgisini taşır.

## 2. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|-----|-----|----------|
| `OrderId` | `string` | İlgili sipariş ID |
| `CustomerId` | `string` | Şikayet sahibi müşteri ID |
| `Complaint` | `string` | Şikayet metni |
| `Status` | `string` | Durum (ör. "Open", "Resolved") |

## Bağlantılar

- [OrderInfo.md](OrderInfo.md) — İlişkili sipariş
- [ToolResult.md](ToolResult.md) — Tool sonuçlarında taşınır
