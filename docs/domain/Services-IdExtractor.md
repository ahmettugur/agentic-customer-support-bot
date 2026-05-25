# IdExtractor

**Dosya:** `Services/IdExtractor.cs`  
**Tür:** `public static class`

Kullanıcı mesajından **deterministic** olarak entity ID'lerini çıkarır — **LLM çağırmaz**, regex kullanır.

---

## Neden LLM kullanmıyoruz?

ID çıkarımı tam olarak regex'in iyi olduğu bir iş:
- ✅ Hızlı (mikrosaniye)
- ✅ Ücretsiz
- ✅ %100 deterministic
- ✅ Halusinasyon riski yok

`PlanningAgent` LLM ile intent tespit eder, sonra `IdExtractor` ID'leri pump eder. İkisi farklı sorunları çözer.

---

## `Extract`

```csharp
public static ExtractedIds Extract(string userQuery)
```

`ExtractedIds` record döner — bulunan ID'leri içerir (yoksa `null`).

---

## ID format tanıma

| Tip | Pattern | Eşleşmeler |
|---|---|---|
| Order | `ORD[-_ ]?N` | `1`, `ORD_1`, `ord-001`, `"ORD 1"` |
| Complaint | `CMP[-_ ]?N` | `1`, `cmp_5` |
| Customer (prefixed) | `CUST[-_ ]?N` | `123`, `cust-7` |
| Customer (numeric fallback) | `\b\d{3,5}\b` | `12345` (3-5 hane) |

**Tüm pattern'ler case-insensitive.**

---

## Numeric customer fallback heuristik

Sadece `3-5 haneli sayı` görünce hemen customer ID demek tehlikeli — sipariş tutarı, tarih, miktar olabilir. Bu yüzden:

**Sadece** şu koşullarda `customer_id` olarak yorumla:
1. Mesajda zaten bir anchor ID var (1030 veya 1001) **VEYA**
2. Mesaj çok kısa (≤4 token) — örn. `"sipariş 12345"` → büyük olasılıkla bir ID

```
"Sipariş 12345 nerede?"          → customer_id = null  (ambiguous)
"1 siparişimi 12345 hesaba"  → customer_id = 12345 (anchor 1 var)
"12345"                          → customer_id = 12345 (kısa mesaj)
```

---

## `BuildHintMessage`

```csharp
public static string BuildHintMessage(ExtractedIds ids)
```

Çıkarılan ID'leri **prompt hint** olarak formatlar; PlanningAgent ve Specialist'ler bunu görür:

```
[ID İPUCU]
- order_id: 1
- customer_id: 12345

[TOOL ÖNCELİĞİ]
order_id mevcutsa order_status_tool kullan; get_last_order_tool gerek yok.
```

Bu hint mesajı LLM çağrısının başına eklenir — model'in deterministic bilgiyi tekrar çıkarmaya çalışıp halüsinasyona düşmesini engeller.

---

## Akış

```
Kullanıcı: "5 nerede"
   ↓
IdExtractor.Extract(query)
   → ExtractedIds { OrderId="5", CustomerId=null, ComplaintId=null }
   ↓
IdExtractor.BuildHintMessage(ids)
   → "[ID İPUCU]\n- order_id: 5\n..."
   ↓
PlanningAgent prompt'una eklenir
   ↓
LLM agent seçimi yapar, hint'i kullanır
```

---

## Test edilebilirlik

Saf static fonksiyon, dış bağımlılık yok:

```csharp
var ids = IdExtractor.Extract("3 siparişim teslim edilmedi");
Assert.Equal("3", ids.OrderId);
Assert.Null(ids.CustomerId);
```
