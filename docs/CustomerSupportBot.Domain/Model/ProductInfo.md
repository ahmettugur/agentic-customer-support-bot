# ProductInfo

**Dosya:** `Model/ProductInfo.cs`  
**Tür:** `record` (immutable)

## 1. Ne İşe Yarar

Bir ürünün domain modelidir — fiyat, stok, isim ve kategori bilgisini taşır.

## 2. Neden Record?

> 💡 **Analiz notu:** `record` tipi seçilmiş çünkü ürün bilgisi immutable bir snapshot'tır — sorgulanan andaki fiyat ve stok durumunu temsil eder. `with` expression ile kolay kopyalama yapılabilir.

## 3. Metotlar / Üyeler

| Parametre | Tip | Açıklama |
| ----------- | ----- | ---------- |
| `Price` | `decimal` | Ürün fiyatı |
| `Stock` | `int` | Stok adedi |
| `Name` | `string` | Ürün adı (varsayılan: "") |
| `Category` | `string` | Kategori (varsayılan: "") |

## Bağlantılar

- [ToolResult.md](ToolResult.md) — Tool sonuçlarında taşınır
