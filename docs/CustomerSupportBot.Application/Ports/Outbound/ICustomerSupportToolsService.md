# ICustomerSupportToolsService

**Kaynak:** `Ports/Outbound/ICustomerSupportToolsService.cs`
**Implementasyon:** [`CustomerSupportToolsService`](../../Services/Tools/CustomerSupportToolsService.md)

## 1. Ne İşe Yarar

`IProductToolsService`, `IOrderToolsService`, `IComplaintToolsService`'i tek bir arayüzde
birleştiren **facade port**. Kendi metodu yoktur — yalnızca üç arayüzü miras alır.

## 2. Hangi Amaçla Kullanılır

`ApprovalGateService` ve `CustomerSupportTeam` (Adapters.Agents) tüm tool fonksiyonlarına tek
bir bağımlılık üzerinden erişmek için bu facade'i inject eder — üç ayrı port'u ayrı ayrı
inject etmek yerine.

## 3. Sorumlulukları

- **Üstlendiği:** Üç alt port'u tek bir sözleşmede toplamak.
- **Üstlenmediği:** Hiçbir iş mantığı — tamamen bir birleştirme (composition) arayüzüdür.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Tools/CustomerSupportToolsService` implemente eder; DI container'da tek
bir kayıt (`AddScoped<ICustomerSupportToolsService, CustomerSupportToolsService>` benzeri) ile
tüm üç alt port da otomatik olarak çözülür.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Interface Segregation Principle ile Facade Pattern'in birlikte kullanımı: her alt port
(`IProductToolsService` vb.) tek başına küçük ve odaklıdır (ör. unit testte yalnızca ürün
tool'larını mock'lamak isteyen bir test yalnızca `IProductToolsService`'i mock'layabilir), ama
gerçek tüketiciler (ajan takımı, onay servisi) genelde HEPSİNE ihtiyaç duyar — facade bu
ikisini uzlaştırır.

## 6. Metotlar / Üyeler

Doğrudan üye yok. Bkz. [`IProductToolsService`](IProductToolsService.md),
[`IOrderToolsService`](IOrderToolsService.md), [`IComplaintToolsService`](IComplaintToolsService.md).

## 7. Bağımlılıklar

Üç alt port arayüzüne bağımlıdır (yukarıda listelenen).
