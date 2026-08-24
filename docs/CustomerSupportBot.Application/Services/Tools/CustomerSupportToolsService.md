# CustomerSupportToolsService

- **Kaynak:** `Services/Tools/CustomerSupportToolsService.cs`
- **Tür:** `public sealed class : ICustomerSupportToolsService`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## 1. Ne İşe Yarar

`ICustomerSupportToolsService` için bir **facade** — tüm çağrıları
[`ProductToolsService`](ProductToolsService.md), [`OrderToolsService`](OrderToolsService.md) ve
[`ComplaintToolsService`](ComplaintToolsService.md)'e olduğu gibi iletir. Kendi iş mantığı
yoktur (tek istisna: statik `HumanHandoffTool`).

## 2. Hangi Amaçla Kullanılır

Tüm müşteri destek tool'larına **tek bir arayüzden** erişmek isteyen tüketiciler (ör. bazı
test senaryoları, ya da tüm tool setini tek bağımlılıkla almak isteyen bir specialist ajan)
için, üç ayrı servisin (`Product`/`Order`/`Complaint`) her birine ayrı ayrı bağımlı olmak
yerine tek bir `ICustomerSupportToolsService`'e bağımlı olunabilmesini sağlamak.

## 3. Sorumlulukları

**Üstlendiği:** Çağrıları doğru alt servise yönlendirmek; bağımlılıksız tek istisna olan
`HumanHandoffTool`'u barındırmak.

**Üstlenmediği:** Hiçbir gerçek iş mantığı — tüm doğrulama/iş kuralları alt servislerdedir
([OrderToolsService.md](OrderToolsService.md), [ComplaintToolsService.md](ComplaintToolsService.md),
[ProductToolsService.md](ProductToolsService.md)).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IProductToolsService`, `IOrderToolsService`, `IComplaintToolsService` — delegasyon hedefleri.
- `ApprovalGateService` (Adapters.Agents) de dahil çoğu tüketici, aslında bu facade yerine
  doğrudan alt servislere (`OrderToolsService` vb.) bağımlıdır — facade, tüm set'e tek seferde
  ihtiyaç duyan (ör. genel amaçlı bir "tüm tool'lar" ajanı) senaryolar için vardır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Facade pattern:** Üç ayrı specialist tool servisi olması (Single Responsibility — her biri
kendi domain'inde), tüketicilerin hepsine ayrı ayrı bağımlı olmak zorunda kalması anlamına
gelmemeli. Bu sınıf, "hepsi bir arada" ihtiyacı olan tüketiciler için ince bir birleştirme
katmanıdır.

### `HumanHandoffTool` neden burada ve neden `static`

Diğer tüm tool'lardan farklı olarak **hiçbir bağımlılığı yoktur** — yalnızca `reason`
alanını doğrulayıp formalize eden, yan etkisiz bir tool. Bu yüzden statik olarak tanımlanmış
ve doğal biçimde bu facade'e yerleştirilmiştir (kendi başına ayrı bir servis/dosya açmaya
değecek karmaşıklıkta değil).

## 6. Metotlar / Üyeler

Tüm metotlar ilgili alt servise **birebir** delege eder — imzalar ve davranış için bkz.
[ProductToolsService.md](ProductToolsService.md) (`ProductInquiryTool`, `ProductListTool`),
[OrderToolsService.md](OrderToolsService.md) (`OrderPlacementTool`, `ValidateOrderActionable`,
`OrderStatusTool`, `GetLastOrderTool`, `GetAllOrdersTool`, `OrderCancelTool`,
`ReturnRequestTool`), [ComplaintToolsService.md](ComplaintToolsService.md)
(`ComplaintStatusTool`, `GetAllComplaintsTool`, `ComplaintRegistrationTool`).

| Üye | Açıklama |
|---|---|
| `HumanHandoffTool(reason)` *(static)* | Tek özgün mantık: `reason` boşsa `ValidationError`; aksi halde temsilci yönlendirme talebini formalize eden bir `ToolResult.Ok` döner (`confidence=1.0`, hiçbir DB yazımı yok — yalnızca niyeti kaydeder). |

## 7. Bağımlılıklar

Constructor injection ile: `IProductToolsService`, `IOrderToolsService`, `IComplaintToolsService`.

## Bağlantılar

- [OrderToolsService.md](OrderToolsService.md), [ComplaintToolsService.md](ComplaintToolsService.md), [ProductToolsService.md](ProductToolsService.md) — gerçek iş mantığının bulunduğu yerler
