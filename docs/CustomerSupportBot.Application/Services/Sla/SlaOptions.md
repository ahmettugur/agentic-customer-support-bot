# SlaOptions

- **Kaynak:** `CustomerSupportBot.Application/Services/Sla/SlaOptions.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Services.Sla`

## Ne işe yarar?

`SlaOptions`, Application/Services/Sla/SlaOptions.cs SLA / Response Time Guardian config. HITL onay kuyruğunda veya açık eskalasyonlarda uzun süre bekleyen kayıtlar için uyarı + ihlal eşikleri. Guardian periyodik tarayıp aksiyon alır. <summary> Bekleyen onay/eskalasyon kayıtları için SLA politikası. </summary> <summary>SLA Guardian aktif mi?</summary> <summary>Tarama frekansı (saniye).</summary> <summary>Bu süreyi aşan onaylar için "warn" eventi yayınlanır — admin panelinde bir uyarı rozeti.</summary> <summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SlaOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `Enabled` (`bool`): İlgili veriyi temsil eden özellik.
- `PollIntervalSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `Approvals` (`ApprovalSlaOptions`): İlgili veriyi temsil eden özellik.
- `Escalations` (`EscalationSlaOptions`): İlgili veriyi temsil eden özellik.
- `WarnAfterSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `BreachAfterSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `OnBreach` (`SlaBreachAction`): İlgili veriyi temsil eden özellik.
- `WarnAfterSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `BreachAfterSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `BoostPriorityOnBreach` (`bool`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
