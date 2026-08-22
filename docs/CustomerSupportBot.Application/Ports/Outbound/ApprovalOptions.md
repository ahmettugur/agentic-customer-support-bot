# ApprovalOptions

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/ApprovalOptions.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`ApprovalOptions`, Application/Services/ApprovalOptions.cs HITL config — appsettings.json > "HumanInTheLoop" bölümünden bind edilir. Hangi tool'ların onay gerektirdiği ve timeout değeri burada tanımlıdır. <summary> HITL ayarları. Approval gate feature'ı Enabled=false ise bypass edilir — Eski davranış korunur (tool direkt çalışır). </summary> <summary>HITL aktif mi? Kapatıldığında tool'lar direkt çalışır.</summary> <summary> Onay isteyen tool adlarının listesi (snake_case). Default: yan etkili iki tool. </summary> <summary>Admin karar vermezse kaç saniye sonra auto-reject.</summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ApprovalOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `Enabled` (`bool`): İlgili veriyi temsil eden özellik.
- `ToolsRequiringApproval` (`List<string>`): İlgili veriyi temsil eden özellik.
- `TimeoutSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `AutoApproveOnTimeout` (`bool`): İlgili veriyi temsil eden özellik.
- `StalePendingHours` (`int`): İlgili veriyi temsil eden özellik.
- `StuckExecutionAfterMinutes` (`int`): İlgili veriyi temsil eden özellik.
- `EscalationEnabled` (`bool`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
