# Tool & Entity Modelleri

**Dosyalar:**
- `Model/ToolResult.cs`
- `Model/OrderInfo.cs`
- `Model/ProductInfo.cs`
- `Model/ComplaintInfo.cs`
- `Model/ExtractedIds.cs`
- `Model/VerifiedEntities.cs`

Tool çıktılarını ve entity referanslarını temsil eder.

---

## ToolResult (yapılandırılmış tool çıktısı)

Her tool **standart bir yapı** döner — başarı/başarısızlık + structured data + öneri.

```csharp
public sealed class ToolResult
{
    public bool Success { get; init; }
    public double Confidence { get; init; }              // 0.0-1.0
    public string Message { get; init; }                 // Türkçe açıklama

    public object? Data { get; init; }                   // Başarıda payload
    public ToolError? Error { get; init; }               // Hatada detay
    public string SuggestedAction { get; init; }         // "proceed"/"ask_user"/"retry"/"escalate"/"abort"
}
```

### SuggestedAction değerleri

| Değer | Tool sonrası ne yapılmalı |
|---|---|
| `proceed` | Specialist devam etsin |
| `ask_user` | Kullanıcıya soru sor |
| `retry` | Tool'u tekrar dene (genelde transient hata) |
| `escalate` | İnsan agent'a yönlendir |
| `abort` | Specialist'i terminate et |

### Factory metodlar

```csharp
ToolResult.Ok(data, message, confidence)
ToolResult.ValidationError(code, message, missingFields)
ToolResult.NotFound(entityKind, identifier)
ToolResult.Conflict(message)
ToolResult.SystemError(message)
```

Her tool implementasyonu bu factory'leri kullanır — exception fırlatmak yerine `ToolResult` döner.

### ToolError

```csharp
public sealed class ToolError
{
    public string Code { get; init; }              // "ORDER_NOT_FOUND"
    public string Category { get; init; }          // "validation"/"not_found"/"conflict"/"business_rule"/"system"
    public string Message { get; init; }
    public List<string> MissingFields { get; init; } = new();
}
```

### Error code listesi (`WellKnown.ToolErrorCodes`)

- `MISSING_REQUIRED_FIELD`
- `PRODUCT_NOT_FOUND`
- `CUSTOMER_NOT_FOUND`
- `ORDER_NOT_FOUND`
- `STOCK_INSUFFICIENT`
- `CUSTOMER_ID_MISMATCH`
- `NO_ORDERS_FOR_CUSTOMER`
- `ORDER_ALREADY_CANCELLED`
- `ORDER_NOT_CANCELLABLE`
- `RETURN_NOT_ELIGIBLE`
- `RETURN_ALREADY_REQUESTED`

---

## Entity Info modelleri

### OrderInfo

```csharp
public class OrderInfo
{
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public string CustomerId { get; set; } = "";
    public string Status { get; set; } = "";        // WellKnown.OrderStatuses
    public DateTime OrderDate { get; set; } = DateTime.Now;

    // İptal bilgileri
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    // İade bilgileri
    public DateTime? ReturnRequestedAt { get; set; }
    public string? ReturnReason { get; set; }
}
```

### ProductInfo

```csharp
public record ProductInfo(decimal Price, int Stock, string Name = "", string Category = "");
```

Hafif DTO — ürün arama sonucu. `Category` Postgres Catalog'dan gelir.

### ComplaintInfo

```csharp
public class ComplaintInfo
{
    public string OrderId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Complaint { get; set; } = "";
    public string Status { get; set; } = "";        // WellKnown.ComplaintStatuses
}
```

---

## ExtractedIds (IdExtractor çıktısı)

```csharp
public class ExtractedIds
{
    public string? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public string? ComplaintId { get; set; }

    public bool HasAny =>
        !string.IsNullOrEmpty(OrderId) ||
        !string.IsNullOrEmpty(CustomerId) ||
        !string.IsNullOrEmpty(ComplaintId);
}
```

Regex sonucu — **henüz DB ile doğrulanmadı**. Sadece format eşleşmesi.

---

## VerifiedEntities (entity grounding)

`ExtractedIds`'tan **bir adım sonra**: DB ile doğrulanmış, kaynağı bilinen entity referansları.

```csharp
public sealed class VerifiedEntities
{
    public VerifiedEntity? OrderId { get; set; }
    public VerifiedEntity? CustomerId { get; set; }
    public VerifiedEntity? ComplaintId { get; set; }

    // Türetilmiş
    public string? DerivedLastOrderId { get; set; }     // Müşterinin en son siparişi
    public int? DerivedOrderCount { get; set; }
}

public sealed class VerifiedEntity
{
    public string Value { get; init; }
    public EntitySource Source { get; init; }
    public EntityVerification Verification { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new();
}

public enum EntitySource
{
    Query,          // Kullanıcı mesajından regex ile çıkarıldı
    History,        // Önceki sohbetten
    SessionState,   // Session.CollectedInfo'dan
    Derived         // Türetilmiş (örn. "son sipariş")
}

public enum EntityVerification
{
    Verified,         // DB'de mevcut
    NotFoundInDb,     // Format doğru ama DB'de yok
    FormatOnly        // Henüz doğrulanmadı (regex eşleşmesi)
}
```

### Neden iki ayrı tip?

| Tip | Kullanım |
|---|---|
| `ExtractedIds` | Hızlı çıkarım — regex sonucu (mikrosaniye) |
| `VerifiedEntities` | DB doğrulanmış + kaynak bilgili (specialist için) |

PlanningAgent `ExtractedIds`'ı kullanır; Specialist tool çağrısı öncesi `VerifiedEntities`'ı oluşturur (DB'ye sorgu atar) ve tool'a parametre olarak verir.

### Attributes ne içerir?

Verified entity için ek bilgi:

```csharp
new VerifiedEntity {
    Value = "5",
    Source = EntitySource.SessionState,
    Verification = EntityVerification.Verified,
    Attributes = {
        ["customer_id"] = "12345",
        ["status"] = "Kargoda",
        ["product"] = "Dell XPS 15"
    }
}
```

Bu sayede specialist tool çağırmadan bile özet bilgiyi yanıtta kullanabilir (cache hit).

---

## Akış

```
Kullanıcı: "5 nerede"
   ↓
IdExtractor.Extract()
   → ExtractedIds { OrderId="5" }  (FormatOnly)
   ↓
PlanningAgent → OrderAgent
   ↓
OrderAgent (Specialist):
   - VerifiedEntities oluştur (DB'de 5 var mı?)
   - Verified ise Attributes'ı doldur
   ↓
OrderAgent.CallTool(order_status_tool, order_id="5")
   → ToolResult.Ok(data: orderInfo, message: "Sipariş kargoda")
   ↓
PostToolReflection → ResponseAgent → kullanıcı
```

---

## Bağlantılar

- [Services-IdExtractor.md](Services-IdExtractor.md) — Regex çıkarımı
- [Model-Specialist.md](Model-Specialist.md) — PreToolCheck VerifiedEntities kullanır
- [WellKnown.md](WellKnown.md) — Tool error code'ları, status string'leri
