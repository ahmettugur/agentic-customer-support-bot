# IComplaintToolsService

**Kaynak:** `Ports/Outbound/IComplaintToolsService.cs`
**Implementasyon:** [`ComplaintToolsService`](../../Services/Tools/ComplaintToolsService.md) (bkz. [`ICustomerSupportToolsService`](ICustomerSupportToolsService.md) facade'i)

## 1. Ne İşe Yarar

Şikayet yönetimi tool'ları için secondary port: şikayet kaydı oluşturma (onaylı), durum
sorgulama, listeleme.

## 2. Hangi Amaçla Kullanılır

`ApprovalGateService` bu tool'ları MAF fonksiyonu olarak sarıp ajanlara sunar (`ComplaintAgent`
kullanır). `ComplaintRegistrationTool` onay akışına girer; diğer ikisi salt-okunurdur ve
doğrudan çalışır.

## 3. Sorumlulukları

- **Üstlendiği:** Şikayet ile ilgili LLM'e açılan üç tool'un iş mantığı (doğrulama, sahiplik
  kontrolü, [`IComplaintRepository`](Persistence/IComplaintRepository.md) çağrısı).
- **Üstlenmediği:** Onay akışının kendisi — `ComplaintRegistrationTool` onay GEREKTİRİR ama
  onayı bekleyip beklememek `ApprovalGateService`'in kararıdır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Tools/ComplaintToolsService` implemente eder;
[`ICustomerSupportToolsService`](ICustomerSupportToolsService.md) facade'inin bir parçasıdır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> `ComplaintStatusTool`'daki `customerId` sahiplik kontrolü içindir ve LLM'den DEĞİL,
> doğrulanmış kimlikten gelir. Başkasının şikayetine erişim, "bulunamadı" ile **AYNI METNİ**
> döner — bu bilinçli bir tasarım: hata mesajı "bu şikayet size ait değil" derse, saldırgan
> deneme-yanılma ile hangi şikayet id'lerinin GERÇEKTEN var olduğunu (ama başkasına ait
> olduğunu) öğrenebilir; aynı mesaj bu bilgi sızıntısını kapatır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `ToolResult ComplaintRegistrationTool(string orderId, string complaintText, string? customerId)` | Yeni şikayet kaydı (onay gerektirir). |
| `ToolResult ComplaintStatusTool(string complaintId, string customerId)` | Şikayet durumu sorgular (salt-okunur, sahiplik kontrollü). |
| `ToolResult GetAllComplaintsTool(string customerId)` | Müşterinin tüm şikayetlerini listeler (salt-okunur). |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ToolResult`'a bağımlıdır.
