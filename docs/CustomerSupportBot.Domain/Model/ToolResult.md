# ToolResult

**Dosya:** `Model/ToolResult.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `ToolError`, `ToolErrorCategories`, `ToolSuggestedActions` (aynı dosyada)

## 1. Ne İşe Yarar

Tüm specialist tool'ların standart **dönüş zarfı**dır — başarı/başarısızlık, güven skoru, mesaj, yapılandırılmış veri veya hata detayı ve aksiyon önerisi taşır.

## 2. Hangi Amaçla Kullanılır

Her tool çağrısı (sipariş sorgula, ürün ara, şikayet kaydet vb.) `ToolResult` döner. Bu sayede specialist agent tool sonucunu düz metin yerine yapılandırılmış olarak görür ve `postToolReflection`'da daha doğru sinyal üretir.

> 💡 **Analiz notu:** API'den dönen `HTTP 200 OK` veya `HTTP 404 Not Found` yanıtı gibi düşün — başarılı mıydı, veri var mı, hata varsa ne tür bir hata, ne yapmalıyız bilgisi standardize edilmiş formatta gelir.

## 3. Sorumlulukları

- ✅ Tool sonucunu standart formatta taşımak
- ✅ Hata taksonomisi sağlamak (Validation, NotFound, Conflict, BusinessRule, System)
- ✅ Factory metotları ile kolay oluşturma (Ok, Pending, ValidationError, NotFound, Conflict, SystemError)
- ✅ `PendingApproval` flag'i ile "iş henüz yapılmadı" ayrımını korumak
- ❌ Tool mantığını yürütmek — sadece sonuç taşır

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `OrderToolsService`, `ProductToolsService`, `ComplaintToolsService` (Application katmanı)
- **Kim tüketir:** Specialist agent'lar (tool sonucunu okur), `WorkflowRunner` (trace'e yazar)

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **`PendingApproval` flag'i kritiktir:** `Success=true` ama `PendingApproval=true` demek "kuyrukta, henüz yapılmadı" demektir. Bu ayrım olmadan bot "siparişiniz oluşturuldu" der ama admin henüz onaylamamıştır — müşteriye yanlış bilgi verilirdi.

> 💡 **Factory metotları:** `ToolResult.Ok()`, `ToolResult.NotFound()` gibi statik factory metotları, her tool'da tutarlı hata yapısı üretmeyi garanti eder.

## 6. Metotlar / Üyeler

### ToolResult

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Success` | `bool` | Başarılı mı? |
| `PendingApproval` | `bool` | HITL kuyruğunda mı? (Success=true ama iş yapılmadı) |
| `Confidence` | `double` | Güven skoru (0.0-1.0) |
| `Message` | `string` | İnsan-dostu özet (Türkçe) |
| `Data` | `object?` | Yapılandırılmış veri (OrderInfo, ProductInfo vb.) |
| `Error` | `ToolError?` | Hata detayı (başarısız ise) |
| `SuggestedAction` | `string` | "proceed", "ask_user", "retry", "escalate", "abort" |

### Factory Metotları

| Metot | Kullanım |
| ------- | ---------- |
| `Ok(message, data?, confidence)` | Başarılı sonuç |
| `Pending(message, data?)` | HITL onay kuyruğunda |
| `ValidationError(message, missingFields)` | Eksik parametre |
| `NotFound(code, message)` | Kayıt bulunamadı |
| `Conflict(code, message, partialConfidence)` | İş kuralı çakışması |
| `SystemError(code, message)` | Sistem hatası → escalate |

### ToolError

| Üye | Açıklama |
| ----- | ---------- |
| `Code` | Makine-okunabilir hata kodu (ör. "ORDER_NOT_FOUND") |
| `Category` | Taksonomi: validation, not_found, conflict, business_rule, system |
| `Message` | İnsan-dostu hata mesajı |
| `MissingFields` | Validation hatalarında eksik alan adları |

## Bağlantılar

- [ApprovalRequest.md](ApprovalRequest.md) — PendingApproval ile HITL bağlantısı
- [SpecialistReasoning.md](SpecialistReasoning.md) — Tool sonucunu kullanan specialist reasoning
- [OrderInfo.md](OrderInfo.md) — Data alanındaki tipik veri
