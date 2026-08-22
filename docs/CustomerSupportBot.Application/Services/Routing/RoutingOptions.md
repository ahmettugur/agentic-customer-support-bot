# RoutingOptions

- **Kaynak:** `CustomerSupportBot.Application/Services/Routing/RoutingOptions.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Services.Routing`

## Ne işe yarar?

`RoutingOptions`, Application/Services/Routing/RoutingOptions.cs Smart Routing & Skills-Based Escalation — appsettings.json "Routing" section üzerinden okunan konfigürasyon. Intent → skill tag mapping ve routing davranışı. <summary>SkillsBasedRouter için konfigürasyon.</summary> <summary>Smart routing açık mı? Kapalıysa router no-op döner.</summary> <summary> Intent → required skill tags mapping. Reasoning trace'inde tespit edilen intent'e göre eklenecek skill etiketleri. </summary> <summary> Müşteri profili anahtar kelimeleri → skill tag mapping. Örn: "VIP" → "vip" (admin notu içeriyorsa). </summary> <summary> LoadBalancing açıksa: aynı match skoruna sahip temsilciler arasında CurrentLoad düşük olan tercih edilir. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`RoutingOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `Enabled` (`bool`): İlgili veriyi temsil eden özellik.
- `LoadBalancingEnabled` (`bool`): İlgili veriyi temsil eden özellik.
- `LanguageWeight` (`double`): İlgili veriyi temsil eden özellik.
- `MinMatchScore` (`double`): İlgili veriyi temsil eden özellik.
- `SeedAgents` (`List<HumanAgent>`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
