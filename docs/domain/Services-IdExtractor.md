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

ID'ler prefix (`ORD-`, `CMP-`) olmadan sadece **4 veya daha fazla haneli sayı** olarak tanınır. Hangi entity türüne ait olduğu, sayının yakın bağlamındaki Türkçe anahtar kelimelere göre belirlenir:

| Entity | Regex deseni | Bağlam kelimesi |
|---|---|---|
| `order_id` | `\b(\d{4,})\b` | `sipari[sş]` (sipariş) |
| `complaint_id` | `\b(\d{4,})\b` | `şikayet` |
| `customer_id` | `\b(\d{4,})\b` | `mü[sş]teri`, veya **sahiplenilmemiş** `numaram` |

Sayı ile bağlam kelimesi arasındaki pencere ±60 karakterdir.

**Tüm kelime desenleri case-insensitive.**

### `numaram` sahiplenmesi (önemli)

`numaram` tek başına müşteri sinyalidir (*"numaram 1025"*), ancak bir entity niteleyicisi tarafından sahiplenilmişse **değildir**: *"sipariş numaram 1041"* = "benim **sipariş** numaram". Bu, `CustomerKeyword` desenindeki negatif lookbehind'larla sağlanır (.NET değişken uzunluklu lookbehind destekler):

```
\bmü[sş]teri|(?<!\bsipari[sş]\w*\s)(?<!\bşikayet\w*\s)\bnumaram\b
```

> ⚠️ **Neden gerekli — canlıda gözlemlenen hata.** Seçim, sayıya en yakın bağlam kelimesine göre yapılır. *"Sipariş numaram 1041"* cümlesinde `numaram` sayıya 1 karakter, `sipariş` ise 9 karakter uzaktadır; sahiplenme dışlanmadığında müşteri kazanıyor ve **sipariş numarası `customer_id` sanılıyordu**.
>
> Etkisi yalnızca prompt hint'iyle sınırlı değildi: [`SessionStateExtractor`](Services-SessionStateExtractor.md) bu sonucu **kalıcı** oturum durumuna yazıyor (`state.CustomerId`). Yani tek bir yanlış sınıflandırma, sonraki **tüm** turlarda `EntityVerifier`'a bir "kaynak" ve `CustomerContextProvider`'a sipariş/şikayet geçmişi sorgusu olarak yanlış müşteri kimliği besliyordu — üstelik `LastMentionedOrderId` de hiç set edilmiyordu.
>
> Regresyon koruması: `IdExtractorTests` (sahiplenme kuralı) ve `SessionStateExtractorTests` (kalıcı state sonucu).

---

## Numeric fallback heuristik

Mesajda bağlam kelimesi yoksa sayı türü belirsizdir. `customer_id` olarak yorumlanması için mesajın **≤5 token** uzunluğunda olması gerekir; daha uzun bağlamlarda sayı atanmaz.

```
"siparişim 4821 nerede?"   → order_id = "4821"   (sipariş bağlamı)
"şikayet 1003 durumu"      → complaint_id = "1003" (şikayet bağlamı)
"1027"                     → customer_id = "1027" (kısa mesaj ≤5 token)
"2024 yılında aldım"       → hiçbiri               (kısa değil, bağlam yok)
```

---

## `BuildHintMessage`

```csharp
public static string? BuildHintMessage(ExtractedIds ids)
```

Çıkarılan ID'leri **prompt hint** olarak formatlar; PlanningAgent ve Specialist'ler bunu görür. Hiç ID çıkarılamamışsa `null` döner.

```
[ENTITY EXTRACTION — deterministik regex ile çıkarıldı]
Kullanıcı mesajından aşağıdaki ID'ler otomatik çıkarıldı. Bu bilgileri KULLAN, tekrar kullanıcıya sorma:
- order_id = "4821"

SİPARİŞ SORGUSU ÖNCELİK KURALI:
  - order_id MEVCUT → 'order_status_tool' kullan (order_id ile sorgula).
  - customer_id TEKRAR SORMA; order_id tek başına yeterlidir.

Not: Ekstraksiyon yanlış görünüyorsa kullanıcıya doğrulat.
```

Bu hint mesajı LLM çağrısının başına eklenir — model'in deterministic bilgiyi tekrar çıkarmaya çalışıp halüsinasyona düşmesini engeller.

---

## Akış

```
Kullanıcı: "4821 siparişim nerede"
   ↓
IdExtractor.Extract(query)
   → ExtractedIds { OrderId="4821", CustomerId=null, ComplaintId=null }
   ↓
IdExtractor.BuildHintMessage(ids)
   → "[ENTITY EXTRACTION...]\n- order_id = \"4821\"\n..."
   ↓
PlanningAgent prompt'una eklenir
   ↓
LLM agent seçimi yapar, hint'i kullanır
```

---

## Test edilebilirlik

Saf static fonksiyon, dış bağımlılık yok:

```csharp
var ids = IdExtractor.Extract("4821 siparişim teslim edilmedi");
Assert.Equal("4821", ids.OrderId);
Assert.Null(ids.CustomerId);

var ids2 = IdExtractor.Extract("şikayet 1003 durumu");
Assert.Equal("1003", ids2.ComplaintId);
```
